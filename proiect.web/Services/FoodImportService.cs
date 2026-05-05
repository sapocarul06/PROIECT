using CsvHelper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace proiect.web.Services
{
    public class FoodImportService
    {
        private readonly string _connectionString;

        private const string ProviderName = "kaggle_top_100_healthiest_foods";

        public FoodImportService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void ImportFoodsFromCsv(string csvPath)
        {
            Console.WriteLine($"Verific fisierul: {csvPath}");

            if (!File.Exists(csvPath))
            {
                Console.WriteLine("Fisierul CSV nu a fost gasit.");
                return;
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                    Console.WriteLine("Conectat la baza de date.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Eroare conexiune DB: " + ex.Message);
                    return;
                }

                if (ImportAlreadyDone(connection))
                {
                    Console.WriteLine("Import deja efectuat.");
                    return;
                }

                using (var reader = new StreamReader(csvPath, Encoding.UTF8))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    var records = csv.GetRecords<dynamic>();
                    int importedCount = 0;

                    foreach (var record in records)
                    {
                        try
                        {
                            var row = (IDictionary<string, object>)record;

                            string name = GetValue(row, "food_name");
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            double? calories = ToNullableDouble(GetValue(row, "calories"));
                            double? protein = ToNullableDouble(GetValue(row, "protein"));
                            double? carbs = ToNullableDouble(GetValue(row, "carbs"));
                            double? fats = ToNullableDouble(GetValue(row, "fat"));

                            string category = GetValue(row, "category");

                            // 👉 AICI clasifici alimentul
                            string mealType = GetMealType(name, category, calories, protein);

                            bool inserted = InsertFood(
                                connection,
                                name,
                                calories,
                                protein,
                                carbs,
                                fats,
                                category,
                                mealType
                            );

                            if (inserted)
                            {
                                importedCount++;
                                Console.WriteLine($"✔ {name} → {mealType}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Eroare la rand: " + ex.Message);
                        }
                    }

                    MarkImportSuccess(connection, importedCount);
                    Console.WriteLine($"Import finalizat: {importedCount} alimente.");
                }
            }
        }

        private bool ImportAlreadyDone(SqlConnection connection)
        {
            using (var command = new SqlCommand(
                @"SELECT COUNT(*) 
                  FROM import_logs 
                  WHERE provider_name = @ProviderName 
                  AND status = 'success'", connection))
            {
                command.Parameters.AddWithValue("@ProviderName", ProviderName);
                return (int)command.ExecuteScalar() > 0;
            }
        }

        private bool InsertFood(
            SqlConnection connection,
            string name,
            double? calories,
            double? protein,
            double? carbs,
            double? fats,
            string category,
            string mealType)
        {
            using (var command = new SqlCommand(
                @"IF NOT EXISTS (SELECT 1 FROM foods WHERE name = @Name)
                  BEGIN
                    INSERT INTO foods (
                        name, calories, protein, carbohydrates, fats, category, source_provider, meal_type
                    )
                    VALUES (
                        @Name, @Calories, @Protein, @Carbs, @Fats, @Category, @Provider, @MealType
                    )
                  END", connection))
            {
                command.Parameters.AddWithValue("@Name", name);
                command.Parameters.AddWithValue("@Calories", ToDbValue(calories));
                command.Parameters.AddWithValue("@Protein", ToDbValue(protein));
                command.Parameters.AddWithValue("@Carbs", ToDbValue(carbs));
                command.Parameters.AddWithValue("@Fats", ToDbValue(fats));
                command.Parameters.AddWithValue("@Category", ToDbValue(category));
                command.Parameters.AddWithValue("@Provider", ProviderName);
                command.Parameters.AddWithValue("@MealType", mealType);

                return command.ExecuteNonQuery() > 0;
            }
        }

        private void MarkImportSuccess(SqlConnection connection, int count)
        {
            using (var command = new SqlCommand(
                @"IF NOT EXISTS (SELECT 1 FROM import_logs WHERE provider_name = @ProviderName)
                  BEGIN
                    INSERT INTO import_logs (provider_name, status, items_imported)
                    VALUES (@ProviderName, 'success', @Count)
                  END", connection))
            {
                command.Parameters.AddWithValue("@ProviderName", ProviderName);
                command.Parameters.AddWithValue("@Count", count);
                command.ExecuteNonQuery();
            }
        }

        private static string GetValue(IDictionary<string, object> row, string key)
        {
            return row.TryGetValue(key, out var value) ? value?.ToString() : null;
        }

        private static double? ToNullableDouble(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Replace(",", ".").Trim();

            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            return null;
        }

        private static object ToDbValue(object value)
        {
            return value ?? DBNull.Value;
        }

        // 🔥 FUNCTIA DE CLASIFICARE
        private static string GetMealType(string name, string category, double? calories, double? protein)
        {
            string text = (name + " " + category).ToLower();
            category = category?.ToLower() ?? "";

            // 🍎 GUSTARE
            if (category.Contains("fruit") ||
                category.Contains("snack") ||
                text.Contains("apple") ||
                text.Contains("banana") ||
                text.Contains("cookie") ||
                text.Contains("cake") ||
                text.Contains("chocolate") ||
                (calories.HasValue && calories < 150))
            {
                return "gustare";
            }

            // 🥐 MIC DEJUN
            if (category.Contains("dairy") ||
                category.Contains("egg") ||
                text.Contains("milk") ||
                text.Contains("yogurt") ||
                text.Contains("cereal") ||
                text.Contains("oat") ||
                text.Contains("bread") ||
                (calories.HasValue && calories >= 150 && calories < 350))
            {
                return "mic_dejun";
            }

            // 🍽️ PRÂNZ
            if (category.Contains("meat") ||
                category.Contains("poultry") ||
                category.Contains("beef") ||
                category.Contains("pork") ||
                text.Contains("chicken") ||
                text.Contains("beef") ||
                text.Contains("rice") ||
                text.Contains("pasta") ||
                (calories.HasValue && calories >= 350 && calories < 700))
            {
                return "pranz";
            }

            // 🍲 CINĂ
            if (text.Contains("fish") ||
                text.Contains("soup") ||
                text.Contains("salad") ||
                (protein.HasValue && protein > 20) ||
                (calories.HasValue && calories >= 700))
            {
                return "cina";
            }

            // 🔥 FALLBACK INTELIGENT
            if (calories.HasValue)
            {
                if (calories < 150) return "gustare";
                if (calories < 350) return "mic_dejun";
                if (calories < 700) return "pranz";
                return "cina";
            }

            // fallback final (fără date)
            return "gustare";
        }
    }
}