using Microsoft.Data.SqlClient;
using System;

namespace proiect.web.Services
{
    public class MealPlanService
    {
        private readonly string _connectionString;

        public MealPlanService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void GenerateWeeklyPlan(int userId)
        {
            var meals = new[] { "mic_dejun", "pranz", "cina", "gustare" };

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                // 🔴 curăță planul vechi
                var deleteCmd = new SqlCommand(
                    "DELETE FROM meal_plan WHERE user_id = @UserId", connection);
                deleteCmd.Parameters.AddWithValue("@UserId", userId);
                deleteCmd.ExecuteNonQuery();

                for (int day = 1; day <= 7; day++)
                {
                    foreach (var meal in meals)
                    {
                        var food = GetRandomFoodByMeal(connection, meal);

                        if (food != null)
                        {
                            InsertMealPlan(connection, userId, day, meal, food.Id);

                            Console.WriteLine($"Ziua {day} - {meal}: {food.Name}");
                        }
                    }
                }
            }
        }

        private Food GetRandomFoodByMeal(SqlConnection connection, string mealType)
        {
            var command = new SqlCommand(
                @"SELECT TOP 1 id, name 
                  FROM foods 
                  WHERE meal_type = @MealType
                  ORDER BY NEWID()", connection);

            command.Parameters.AddWithValue("@MealType", mealType);

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return new Food
                    {
                        Id = (int)reader["id"],
                        Name = reader["name"].ToString()
                    };
                }
            }

            return null;
        }

        private void InsertMealPlan(SqlConnection connection, int userId, int day, string mealType, int foodId)
        {
            var command = new SqlCommand(
                @"INSERT INTO meal_plan (user_id, day_of_week, meal_type, food_id)
                  VALUES (@UserId, @Day, @MealType, @FoodId)", connection);

            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Day", day);
            command.Parameters.AddWithValue("@MealType", mealType);
            command.Parameters.AddWithValue("@FoodId", foodId);

            command.ExecuteNonQuery();
        }
    }

    // model simplu
    public class Food
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}