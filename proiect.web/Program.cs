using proiect.web.Services;
using System.Net;
using System.Net.Mail;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=PlanAlimentarDB;Trusted_Connection=True;TrustServerCertificate=True;";

// Repository builder.Services.AddScoped<IAlimentRepository>(sp => new AlimentRepository(connectionString));

// External Food Service builder.Services.AddHttpClient<IExternalFoodService, ExternalFoodService>();

// Meal Plan Service builder.Services.AddScoped<IMealPlanService, MealPlanService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

app.MapPost("/email", async (string recipientEmail) =>
{
    string senderEmail = "sapocarul06@gmail.com";
    string appPassword = "krke fauh zceh ghsq";


    using (var client = new SmtpClient("smtp.gmail.com", 587))
    {
        client.EnableSsl = true;
        client.UseDefaultCredentials = false;
        client.Credentials = new NetworkCredential(senderEmail, appPassword);

        MailMessage message = new MailMessage(senderEmail, recipientEmail)
        {
            Subject = "Planul tău alimentar",
            Body = "Planul tău alimentar personalizat este gata!"
        };

        try
        {
            await client.SendMailAsync(message);
            return Results.Ok("Email trimis!");
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Eroare: {ex.Message}");
        }
    }
});
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

   
    var importService = new proiect.web.Services.FoodImportService(connectionString);

    string csvPath = @"C:\Users\Madalin\Desktop\PROIECT\proiect.web\data\providers\Food_Nutrition_Dataset.csv";

    importService.ImportFoodsFromCsv(csvPath);
}
app.Run();