using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Reflection.Metadata;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace proiect.web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly string _connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=PlanAlimentarDB;Trusted_Connection=True;TrustServerCertificate=True;";
        private readonly IWebHostEnvironment _env;
        private readonly IAntiforgery _antiforgery;

        [BindProperty]
        public string Sex { get; set; }

        [BindProperty]
        public string EmailDestinatar { get; set; }

        [BindProperty]
        public int Varsta { get; set; }

        [BindProperty]
        public double Greutate { get; set; }

        [BindProperty]
        public int Inaltime { get; set; }

        [BindProperty]
        public string Activitate { get; set; } = "moderat";

        [BindProperty]
        public string Obiectiv { get; set; } = "mentime";

        public bool HasCalculated { get; set; }
        public string MessageResult { get; set; }
        public string ErrorMessage { get; set; }
        public string EmailErrorMessage { get; set; }
        public string ResultSummary { get; set; }
        public string PdfUrl { get; set; }
        public double TargetProteins { get; set; }
        public double DailyCalories { get; set; }
        public Dictionary<int, List<MealInfo>> WeeklyPlan { get; set; } = new();
        public Dictionary<int, DaySummary> DaySummaries { get; set; } = new();

        public IndexModel(IWebHostEnvironment env, IAntiforgery antiforgery)
        {
            _env = env;
            _antiforgery = antiforgery;
        }

        public void OnGet()
        {
            // Restore meal plan from TempData if available (after swap operation)
            var weeklyPlanJson = TempData["WeeklyPlan"] as string;
            var daySummariesJson = TempData["DaySummaries"] as string;
            
            if (!string.IsNullOrEmpty(weeklyPlanJson))
            {
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadNumberHandling = System.Text.Json.JsonNumberHandling.AllowReadingFromString
                };
                
                WeeklyPlan = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, List<MealInfo>>>(weeklyPlanJson, options);
                DaySummaries = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, DaySummary>>(daySummariesJson, options);
                
                if (TempData["DailyCalories"] != null)
                    DailyCalories = double.Parse(TempData["DailyCalories"].ToString());
                if (TempData["TargetProteins"] != null)
                    TargetProteins = double.Parse(TempData["TargetProteins"].ToString());
                if (TempData["HasCalculated"] != null)
                    HasCalculated = bool.Parse(TempData["HasCalculated"].ToString());
                if (TempData["ResultSummary"] != null)
                    ResultSummary = TempData["ResultSummary"].ToString();
                if (TempData["PdfUrl"] != null)
                    PdfUrl = TempData["PdfUrl"].ToString();
            }
        }

        [ValidateAntiForgeryToken]
        public IActionResult OnPostGeneratePlan()
        {
            HasCalculated = true;
            ErrorMessage = "";

            if (Greutate <= 0 || Inaltime <= 0 || Varsta <= 0)
            {
                ErrorMessage = "Date invalide!";
                HasCalculated = false;
                return Page();
            }

            // Step 1: Calculate BMR using Mifflin-St Jeor formula
            double bmr = CalculateBMR(Sex, Greutate, Inaltime, Varsta);

            // Step 2: Calculate TDEE based on activity level
            double activityMultiplier = GetActivityMultiplier(Activitate);
            double tdee = bmr * activityMultiplier;

            // Step 3: Calculate daily calories based on objective
            double dailyCalories = 0;
            string objectiveText = "";

            if (Obiectiv == "mentime")
            {
                dailyCalories = tdee;
                objectiveText = "Sa-si mentina greutatea";
            }
            else if (Obiectiv == "slabire_usoara")
            {
                dailyCalories = tdee - 300;
                objectiveText = "Slabire usoara (300 kcal deficit)";
            }
            else if (Obiectiv == "slabire_normala")
            {
                dailyCalories = tdee - 500;
                objectiveText = "Slabire normala (500 kcal deficit)";
            }

            // Ensure minimum calorie limits
            double minCalories = Sex == "M" ? 1500 : 1200;
            if (dailyCalories < minCalories)
            {
                dailyCalories = minCalories;
            }

            // Step 4: Calculate target proteins (2g per kg of body weight)
            TargetProteins = Greutate * 2.0;
            DailyCalories = dailyCalories;

            // Calculate weekly weight loss
            double weeklyCalorieDeficit = 0;
            if (Obiectiv == "mentime")
            {
                weeklyCalorieDeficit = 0;
            }
            else
            {
                weeklyCalorieDeficit = (tdee - dailyCalories) * 7;
            }

            // Build result summary with all calculations
            string activityLabel = GetActivityLabel(Activitate);
            ResultSummary = $"<strong>🔥 Calorii:</strong> BMR: {bmr:F0} kcal | Activitate: {activityLabel} ({activityMultiplier}x) | TDEE: {tdee:F0} kcal | Tinta: {dailyCalories:F0} kcal/zi " +
                $"({objectiveText})<br/>" +
                $"<strong>🥩 Proteine:</strong> {TargetProteins:F0}g/zi (2g × {Greutate:F0}kg)<br/>";

            if (Obiectiv != "mentime")
            {
                ResultSummary += $"📉 Deficitul saptamanal: {weeklyCalorieDeficit:F0} kcal | Pierdere estimata: ~{(weeklyCalorieDeficit / 7700):F2} kg/saptamana";
            }

            // Generate meal plan from database
            try
            {
                GenerateMealPlan(dailyCalories);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Eroare la generarea planului: {ex.Message}";
                HasCalculated = false;
                return Page();
            }

            if (WeeklyPlan.Count == 0)
            {
                ErrorMessage = "Nu s-au gasit alimente in baza de date. Importa mai intai alimentele!";
                return Page();
            }

            // Generate PDF Report after meal plan is created
            try
            {
                GenerateReportPDF();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Eroare la generarea PDF: {ex.Message}";
                PdfUrl = string.Empty;
            }

            // Save the meal plan to TempData for later use in swap operations
            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = null
            };
            
            TempData["WeeklyPlan"] = System.Text.Json.JsonSerializer.Serialize(WeeklyPlan, jsonOptions);
            TempData["DaySummaries"] = System.Text.Json.JsonSerializer.Serialize(DaySummaries, jsonOptions);
            TempData["DailyCalories"] = dailyCalories.ToString();
            TempData["TargetProteins"] = TargetProteins.ToString();
            TempData["HasCalculated"] = true;
            TempData["ResultSummary"] = ResultSummary;
            TempData["PdfUrl"] = PdfUrl;

            MessageResult = $"Planul tau alimentar personalizat a fost generat!";
            return Page();
        }

        [IgnoreAntiforgeryToken]
        public JsonResult OnGetAlternatives(int foodId, string mealType)
        {
            var alternatives = GetAlternativeFoods(foodId, mealType);
            return new JsonResult(alternatives);
        }

        [IgnoreAntiforgeryToken]
        public JsonResult OnPostSwapFood(int day, int mealIndex, int newFoodId, int originalFoodId, string mealType, double dailyCalories, double targetProteins)
        {
            try
            {
                // Restore meal plan from TempData first
                var weeklyPlanJson = TempData["WeeklyPlan"] as string;
                var daySummariesJson = TempData["DaySummaries"] as string;
                
                if (!string.IsNullOrEmpty(weeklyPlanJson))
                {
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadNumberHandling = System.Text.Json.JsonNumberHandling.AllowReadingFromString
                    };
                    
                    WeeklyPlan = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, List<MealInfo>>>(weeklyPlanJson, jsonOptions);
                    DaySummaries = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, DaySummary>>(daySummariesJson, jsonOptions);
                    
                    if (TempData["DailyCalories"] != null)
                        DailyCalories = double.Parse(TempData["DailyCalories"].ToString());
                    if (TempData["TargetProteins"] != null)
                        TargetProteins = double.Parse(TempData["TargetProteins"].ToString());
                    if (TempData["HasCalculated"] != null)
                        HasCalculated = bool.Parse(TempData["HasCalculated"].ToString());
                    ResultSummary = TempData["ResultSummary"]?.ToString() ?? "";
                    PdfUrl = TempData["PdfUrl"]?.ToString() ?? "";
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    
                    // Get the new food details
                    var command = new SqlCommand(
                        @"SELECT id, name, calories, protein, meal_type 
                          FROM foods 
                          WHERE id = @Id", connection);
                    command.Parameters.AddWithValue("@Id", newFoodId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var newMeal = new MealInfo
                            {
                                Id = (int)reader["id"],
                                Name = reader["name"].ToString(),
                                Calories = double.Parse(reader["calories"].ToString()),
                                Protein = double.Parse(reader["protein"].ToString()),
                                MealType = reader["meal_type"].ToString()
                            };

                            // Find and replace the food directly in the existing WeeklyPlan
                            if (WeeklyPlan.ContainsKey(day))
                            {
                                var dayMeals = WeeklyPlan[day];
                                int foundIndex = -1;
                                
                                // First try to find by originalFoodId
                                for (int i = 0; i < dayMeals.Count; i++)
                                {
                                    if (dayMeals[i].Id == originalFoodId)
                                    {
                                        foundIndex = i;
                                        break;
                                    }
                                }
                                
                                // If not found by ID, try to find by mealIndex as fallback
                                if (foundIndex == -1 && mealIndex >= 0 && mealIndex < dayMeals.Count)
                                {
                                    foundIndex = mealIndex;
                                }
                                
                                if (foundIndex >= 0 && foundIndex < dayMeals.Count)
                                {
                                    // Check for duplicates in the same day before swapping
                                    var usedFoodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    
                                    for (int i = 0; i < dayMeals.Count; i++)
                                    {
                                        if (i != foundIndex) // Skip the food we're replacing
                                        {
                                            usedFoodNames.Add(dayMeals[i].Name.ToLower());
                                        }
                                    }
                                    
                                    // Check if the new food would create a duplicate
                                    if (usedFoodNames.Contains(newMeal.Name.ToLower()))
                                    {
                                        return new JsonResult(new { success = false, error = "Acest aliment este deja prezent in planul zilei. Alegeti alta alternativa." });
                                    }
                                    
                                    // Replace the food at the found index with the new food
                                    dayMeals[foundIndex] = newMeal;

                                    // Recalculate day summary
                                    double totalCalories = dayMeals.Sum(m => m.Calories);
                                    double totalProtein = dayMeals.Sum(m => m.Protein);
                                    DaySummaries[day] = new DaySummary
                                    {
                                        TotalCalories = totalCalories,
                                        TotalProtein = totalProtein
                                    };
                                    
                                    try
                                    {
                                        GenerateReportPDF();
                                    }
                                    catch { }

                                    // Save updated plan to TempData
                                    var saveJsonOptions = new System.Text.Json.JsonSerializerOptions
                                    {
                                        WriteIndented = false,
                                        PropertyNamingPolicy = null
                                    };
                                    
                                    TempData["WeeklyPlan"] = System.Text.Json.JsonSerializer.Serialize(WeeklyPlan, saveJsonOptions);
                                    TempData["DaySummaries"] = System.Text.Json.JsonSerializer.Serialize(DaySummaries, saveJsonOptions);
                                    TempData["DailyCalories"] = dailyCalories.ToString();
                                    TempData["TargetProteins"] = targetProteins.ToString();
                                    TempData["HasCalculated"] = true;
                                    TempData["ResultSummary"] = ResultSummary;
                                    TempData["PdfUrl"] = PdfUrl;

                                    return new JsonResult(new { success = true, meal = newMeal, daySummary = DaySummaries[day], pdfUrl = PdfUrl });
                                }
                            }
                        }
                    }
                }

                return new JsonResult(new { success = false, error = "Alimentul nu a fost gasit." });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        private double CalculateBMR(string sex, double weight, int height, int age)
        {
            // Formula Mifflin-St Jeor
            if (sex == "M")
            {
                return (10 * weight) + (6.25 * height) - (5 * age) + 5;
            }
            else
            {
                return (10 * weight) + (6.25 * height) - (5 * age) - 161;
            }
        }

        private double GetActivityMultiplier(string activity)
        {
            return activity switch
            {
                "sedentar" => 1.2,
                "usoareactiv" => 1.375,
                "moderat" => 1.55,
                "activ" => 1.725,
                _ => 1.55
            };
        }

        private string GetActivityLabel(string activity)
        {
            return activity switch
            {
                "sedentar" => "Sedentar",
                "usoareactiv" => "Usor activ",
                "moderat" => "Moderat activ",
                "activ" => "Foarte activ",
                _ => "Moderat activ"
            };
        }

        private void GenerateMealPlan(double dailyCalories)
        {
            // Clear existing plan before generating new one
            WeeklyPlan.Clear();
            DaySummaries.Clear();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();

                    // Verify database connection
                    if (connection.State != System.Data.ConnectionState.Open)
                    {
                        ErrorMessage = "Nu s-a putut conecta la baza de date!";
                        return;
                    }

                    var mealTypes = new[] { "mic_dejun", "pranz", "cina", "gustare" };
                    var mealCalorieTargets = new Dictionary<string, double>
                    {
                        { "mic_dejun", dailyCalories * 0.25 },
                        { "pranz", dailyCalories * 0.35 },
                        { "cina", dailyCalories * 0.30 },
                        { "gustare", dailyCalories * 0.10 }
                    };

                    for (int day = 1; day <= 7; day++)
                    {
                        var dayMeals = new List<MealInfo>();
                        var usedFoodNames = new HashSet<string>();

                        double dayTotalCalories = 0;
                        double dayTotalProtein = 0;

                        foreach (var mealType in mealTypes)
                        {
                            double targetCalories = mealCalorieTargets[mealType];
                            double mealAccumulated = 0;
                            int foodCount = 0;

                            var foods = GetFoodsByMeal(connection, mealType, targetCalories);

                            bool addedSomething = false;

                            foreach (var meal in foods)
                            {
                                string currentName = meal.Name.ToLower();

                                if (usedFoodNames.Any(x => IsSimilar(x, currentName)))
                                    continue;

                                double potentialTotal = dayTotalCalories + meal.Calories;

                                if (potentialTotal > dailyCalories + 150)
                                    break;

                                dayMeals.Add(meal);
                                usedFoodNames.Add(currentName);
                                addedSomething = true;

                                dayTotalCalories += meal.Calories;
                                dayTotalProtein += meal.Protein;
                                mealAccumulated += meal.Calories;
                                foodCount++;

                                if ((mealAccumulated >= targetCalories * 0.85 && mealAccumulated <= targetCalories) || foodCount >= 3)
                                    break;
                            }

                            if (!addedSomething)
                            {
                                var fallback = GetAnyFood(connection, mealType);

                                if (fallback != null)
                                {
                                    string fallbackName = fallback.Name.ToLower();

                                    if (!usedFoodNames.Any(x => IsSimilar(x, fallbackName)))
                                    {
                                        dayMeals.Add(fallback);
                                        usedFoodNames.Add(fallbackName);

                                        dayTotalCalories += fallback.Calories;
                                        dayTotalProtein += fallback.Protein;
                                    }
                                }
                            }
                        }

                        WeeklyPlan[day] = dayMeals;
                        DaySummaries[day] = new DaySummary
                        {
                            TotalCalories = dayTotalCalories,
                            TotalProtein = dayTotalProtein
                        };
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Eroare la generarea planului: {ex.Message}";
                    throw; // Re-throw to be caught by the caller
                }
            }
        }

        private void GenerateMealPlanForSwap(Dictionary<int, List<MealInfo>> weeklyPlan, Dictionary<int, DaySummary> daySummaries, double dailyCalories, double targetProteins)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();

                    var mealTypes = new[] { "mic_dejun", "pranz", "cina", "gustare" };
                    var mealCalorieTargets = new Dictionary<string, double>
                    {
                        { "mic_dejun", dailyCalories * 0.25 },
                        { "pranz", dailyCalories * 0.35 },
                        { "cina", dailyCalories * 0.30 },
                        { "gustare", dailyCalories * 0.10 }
                    };

                    for (int day = 1; day <= 7; day++)
                    {
                        var dayMeals = new List<MealInfo>();
                        var usedFoodNames = new HashSet<string>();

                        double dayTotalCalories = 0;
                        double dayTotalProtein = 0;

                        foreach (var mealType in mealTypes)
                        {
                            double targetCalories = mealCalorieTargets[mealType];
                            double mealAccumulated = 0;
                            int foodCount = 0;

                            var foods = GetFoodsByMeal(connection, mealType, targetCalories);

                            bool addedSomething = false;

                            foreach (var meal in foods)
                            {
                                string currentName = meal.Name.ToLower();

                                if (usedFoodNames.Any(x => IsSimilar(x, currentName)))
                                    continue;

                                double potentialTotal = dayTotalCalories + meal.Calories;

                                if (potentialTotal > dailyCalories + 150)
                                    break;

                                dayMeals.Add(meal);
                                usedFoodNames.Add(currentName);
                                addedSomething = true;

                                dayTotalCalories += meal.Calories;
                                dayTotalProtein += meal.Protein;
                                mealAccumulated += meal.Calories;
                                foodCount++;

                                if ((mealAccumulated >= targetCalories * 0.85 && mealAccumulated <= targetCalories) || foodCount >= 3)
                                    break;
                            }

                            if (!addedSomething)
                            {
                                var fallback = GetAnyFood(connection, mealType);

                                if (fallback != null)
                                {
                                    string fallbackName = fallback.Name.ToLower();

                                    if (!usedFoodNames.Any(x => IsSimilar(x, fallbackName)))
                                    {
                                        dayMeals.Add(fallback);
                                        usedFoodNames.Add(fallbackName);

                                        dayTotalCalories += fallback.Calories;
                                        dayTotalProtein += fallback.Protein;
                                    }
                                }
                            }
                        }

                        weeklyPlan[day] = dayMeals;
                        daySummaries[day] = new DaySummary
                        {
                            TotalCalories = dayTotalCalories,
                            TotalProtein = dayTotalProtein
                        };
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue
                }
            }
        }

        private List<MealInfo> GetFoodsByMeal(SqlConnection connection, string mealType, double targetCalories)
        {
            var foods = new List<MealInfo>();

            var command = new SqlCommand(
                @"SELECT id, name, calories, protein, meal_type 
                  FROM foods 
                  WHERE meal_type = @MealType AND calories <= @MaxCalories
                  ORDER BY NEWID()", connection);

            command.Parameters.AddWithValue("@MealType", mealType);
            command.Parameters.AddWithValue("@MaxCalories", (int)(targetCalories * 1.5));

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read() && foods.Count < 3)
                {
                    foods.Add(new MealInfo
                    {
                        Id = (int)reader["id"],
                        Name = reader["name"].ToString(),
                        Calories = double.Parse(reader["calories"].ToString()),
                        Protein = double.Parse(reader["protein"].ToString()),
                        MealType = reader["meal_type"].ToString()
                    });
                }
            }

            return foods;
        }

        private MealInfo GetAnyFood(SqlConnection connection, string mealType)
        {
            var command = new SqlCommand(
                @"SELECT TOP 1 id, name, calories, protein, meal_type
                  FROM foods
                  WHERE meal_type = @MealType
                  ORDER BY NEWID()", connection);

            command.Parameters.AddWithValue("@MealType", mealType);

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return new MealInfo
                    {
                        Id = (int)reader["id"],
                        Name = reader["name"].ToString(),
                        Calories = double.Parse(reader["calories"].ToString()),
                        Protein = double.Parse(reader["protein"].ToString()),
                        MealType = reader["meal_type"].ToString()
                    };
                }
            }

            return null;
        }

        private List<MealInfo> GetAlternativeFoods(int currentFoodId, string mealType)
        {
            var alternatives = new List<MealInfo>();

            using (var connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();

                    // First get the current food to know its calorie range
                    var currentFoodCommand = new SqlCommand(
                        @"SELECT calories FROM foods WHERE id = @Id", connection);
                    currentFoodCommand.Parameters.AddWithValue("@Id", currentFoodId);

                    double currentCalories = 0;
                    using (var reader = currentFoodCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            currentCalories = double.Parse(reader["calories"].ToString());
                        }
                    }

                    // Get alternative foods with similar calories (±150 kcal) and same meal type
                    var command = new SqlCommand(
                        @"SELECT TOP 5 id, name, calories, protein, meal_type 
                          FROM foods 
                          WHERE meal_type = @MealType 
                            AND id != @CurrentId
                            AND calories BETWEEN @MinCal AND @MaxCal
                          ORDER BY NEWID()", connection);

                    command.Parameters.AddWithValue("@MealType", mealType);
                    command.Parameters.AddWithValue("@CurrentId", currentFoodId);
                    command.Parameters.AddWithValue("@MinCal", Math.Max(0, currentCalories - 150));
                    command.Parameters.AddWithValue("@MaxCal", currentCalories + 150);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            alternatives.Add(new MealInfo
                            {
                                Id = (int)reader["id"],
                                Name = reader["name"].ToString(),
                                Calories = double.Parse(reader["calories"].ToString()),
                                Protein = double.Parse(reader["protein"].ToString()),
                                MealType = reader["meal_type"].ToString()
                            });
                        }
                    }

                    // If no alternatives found in calorie range, get any from same meal type
                    if (alternatives.Count == 0)
                    {
                        var fallbackCommand = new SqlCommand(
                            @"SELECT TOP 5 id, name, calories, protein, meal_type 
                              FROM foods 
                              WHERE meal_type = @MealType AND id != @CurrentId
                              ORDER BY NEWID()", connection);
                        fallbackCommand.Parameters.AddWithValue("@MealType", mealType);
                        fallbackCommand.Parameters.AddWithValue("@CurrentId", currentFoodId);

                        using (var reader = fallbackCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                alternatives.Add(new MealInfo
                                {
                                    Id = (int)reader["id"],
                                    Name = reader["name"].ToString(),
                                    Calories = double.Parse(reader["calories"].ToString()),
                                    Protein = double.Parse(reader["protein"].ToString()),
                                    MealType = reader["meal_type"].ToString()
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error but return empty list
                }
            }

            return alternatives;
        }

        private void GenerateReportPDF()
        {
            // Ensure wwwroot directory exists
            string wwwrootPath = _env.WebRootPath;
            if (string.IsNullOrEmpty(wwwrootPath))
            {
                wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            }
            Directory.CreateDirectory(wwwrootPath);

            // Create rapoarte directory
            string folderRapoarte = Path.Combine(wwwrootPath, "rapoarte");
            Directory.CreateDirectory(folderRapoarte);

            // Generate unique filename
            string fileName = $"Plan_Alimentar_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = Path.Combine(folderRapoarte, fileName);

            try
            {
                using (var writer = new PdfWriter(filePath))
                using (var pdfDocument = new PdfDocument(writer))
                using (var document = new iText.Layout.Document(pdfDocument)) 
                {
                    document.SetMargins(36, 36, 36, 36);

                    // Title
                    var title = new Paragraph($"📋 Plan Alimentar Personalizat - {Nume}")
                        .SetFontSize(22)
                        .SetBold()
                        .SetTextAlignment(TextAlignment.CENTER);
                    document.Add(title);

                    // Date and Basic Info
                    var dateInfo = new Paragraph($"Data: {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .SetFontSize(10)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetMarginBottom(5);
                    document.Add(dateInfo);

                    // Summary Box
                    var summaryBox = new Paragraph()
                        .SetFontSize(11)
                        .SetMarginBottom(20);
                    summaryBox.Add($"🔥 Calorii zilnice: {DailyCalories:F0} kcal\n");
                    summaryBox.Add($"🥩 Proteine zilnice: {TargetProteins:F0}g\n");
                    summaryBox.Add($"👤 Varsta: {Varsta} ani | Sex: {(Sex == "M" ? "Masculin" : "Feminin")}\n");
                    summaryBox.Add($"📏 Greutate: {Greutate:F0} kg | Inaltime: {Inaltime} cm");
                    document.Add(summaryBox);

                    document.Add(new Paragraph("").SetMarginBottom(15));

                    // Weekly Plan
                    var weekTitle = new Paragraph("📅 PLANUL ALIMENTAR PE 7 ZILE")
                        .SetFontSize(14)
                        .SetBold()
                        .SetMarginBottom(15);
                    document.Add(weekTitle);

                    string[] dayNames = { "", "Luni", "Marti", "Miercuri", "Joi", "Vineri", "Sambata", "Duminica" };

                    for (int day = 1; day <= 7; day++)
                    {
                        if (WeeklyPlan.ContainsKey(day))
                        {
                            var dayMeals = WeeklyPlan[day];
                            var daySummary = DaySummaries[day];

                            // Day header
                            var dayHeader = new Paragraph($"🗓️ ZIUA {day} - {dayNames[day]}")
                                .SetFontSize(12)
                                .SetBold()
                                .SetMarginTop(10)
                                .SetMarginBottom(10);
                            document.Add(dayHeader);

                            // Meals table
                            var mealsTable = new Table(4).UseAllAvailableWidth();
                            mealsTable.SetMarginBottom(10);

                            // Header cells
                            mealsTable.AddHeaderCell(new Cell().Add(new Paragraph("Masa 🍽️").SetBold()));
                            mealsTable.AddHeaderCell(new Cell().Add(new Paragraph("Aliment").SetBold()));
                            mealsTable.AddHeaderCell(new Cell().Add(new Paragraph("Calorii").SetBold()));
                            mealsTable.AddHeaderCell(new Cell().Add(new Paragraph("Proteine (g)").SetBold()));

                            // Data cells
                            foreach (var meal in dayMeals)
                            {
                                mealsTable.AddCell(new Cell().Add(new Paragraph(meal.GetMealLabel())));
                                mealsTable.AddCell(new Cell().Add(new Paragraph(meal.Name)));
                                mealsTable.AddCell(new Cell().Add(new Paragraph($"{meal.Calories:F0}")));
                                mealsTable.AddCell(new Cell().Add(new Paragraph($"{meal.Protein:F1}")));
                            }

                            document.Add(mealsTable);

                            // Day summary
                            var daySummaryPara = new Paragraph()
                                .SetTextAlignment(TextAlignment.RIGHT)
                                .SetFontSize(11)
                                .SetBold()
                                .SetMarginBottom(15);
                            daySummaryPara.Add($"📊 TOTAL: {daySummary.TotalCalories:F0} kcal | {daySummary.TotalProtein:F1}g proteina");
                            document.Add(daySummaryPara);
                        }
                    }

                    // Footer
                    document.Add(new Paragraph("").SetMarginBottom(15));
                    var footer = new Paragraph("✅ Plan generat automat. Consulta un nutritionist pentru recomandari personalizate!")
                        .SetFontSize(9)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .SetItalic();
                    document.Add(footer);
                }

                PdfUrl = $"/rapoarte/{fileName}";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Eroare la crearea PDF: {ex.Message}";
                PdfUrl = string.Empty;
            }
        }

        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OnPostSendEmail()
        {
            EmailErrorMessage = "";
            if (string.IsNullOrWhiteSpace(EmailDestinatar))
            {
                EmailErrorMessage = "Introdu un email valid!";
                return Page();
            }
            if (!IsValidEmail(EmailDestinatar))
            {
                EmailErrorMessage = "Formatul email-ului nu este corect!";
                return Page();
            }

            try
            {
                string senderEmail = "sapocarul06@gmail.com";
                string appPassword = "krke fauh zceh ghsq";

                using (var client = new SmtpClient("smtp.gmail.com", 587))
                {
                    client.EnableSsl = true;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(senderEmail, appPassword);

                    string mesaj = @"Stiai ca cea mai mare bariera intre tine si o transformare de 10 kilograme intr-o singura luna nu este lipsa de vointa, ci lipsa de organizare. Majoritatea regimurilor esueaza pentru ca sunt greu de urmarit. Dar daca tot ce trebuie sa mananci ar fi fost deja decis, calculat si optimizat pentru corpul tau, inainte ca macar sa simti senzatia de foame?";

                    MailMessage message = new MailMessage(senderEmail, EmailDestinatar)
                    {
                        Subject = "🔥 Transformarea ta incepe aici",
                        Body = mesaj
                    };

                    await client.SendMailAsync(message);
                }

                MessageResult = "Email trimis cu succes!";
            }
            catch (Exception ex)
            {
                EmailErrorMessage = $"Eroare la trimiterea email-ului: {ex.Message}";
            }

            // Dupa trimiterea email-ului, afiseaza din nou planul alimentar daca exista
            // Nu regeneram planul, doar il afisam pe cel existent din TempData
            if (HasCalculated)
            {
                // Restore data from TempData to show the existing plan
                if (TempData["WeeklyPlan"] != null)
                {
                    WeeklyPlan = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, List<MealInfo>>>(TempData["WeeklyPlan"].ToString());
                    DaySummaries = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, DaySummary>>(TempData["DaySummaries"].ToString());
                    DailyCalories = double.Parse(TempData["DailyCalories"].ToString());
                    TargetProteins = double.Parse(TempData["TargetProteins"].ToString());
                    ResultSummary = TempData["ResultSummary"].ToString();
                    PdfUrl = TempData["PdfUrl"].ToString();
                    
                    // Re-save to TempData since reading it removes it
                    TempData["WeeklyPlan"] = System.Text.Json.JsonSerializer.Serialize(WeeklyPlan);
                    TempData["DaySummaries"] = System.Text.Json.JsonSerializer.Serialize(DaySummaries);
                    TempData["DailyCalories"] = DailyCalories;
                    TempData["TargetProteins"] = TargetProteins;
                    TempData["HasCalculated"] = true;
                    TempData["ResultSummary"] = ResultSummary;
                    TempData["PdfUrl"] = PdfUrl;
                }
            }

            return Page();
        }
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private bool IsSimilar(string a, string b)
        {
            return a.Contains(b) || b.Contains(a);
        }

        // Add a property to get the user's name - you need to capture this from form
        public string Nume { get; set; } = "Utilizator"; // Default, add to form binding if needed
    }

    public class MealInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Calories { get; set; }
        public double Protein { get; set; }
        public string MealType { get; set; }

        public string GetMealEmoji()
        {
            return MealType switch
            {
                "mic_dejun" => "🌅",
                "pranz" => "☀️",
                "cina" => "🌙",
                "gustare" => "🍎",
                _ => "🍽️"
            };
        }

        public string GetMealLabel()
        {
            return MealType switch
            {
                "mic_dejun" => "MIC DEJUN",
                "pranz" => "PRANZ",
                "cina" => "CINA",
                "gustare" => "GUSTARE",
                _ => "MANCARE"
            };
        }
    }

    public class DaySummary
    {
        public double TotalCalories { get; set; }
        public double TotalProtein { get; set; }
    }
}