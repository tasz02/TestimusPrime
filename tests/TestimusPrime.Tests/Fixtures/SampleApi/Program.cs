var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.MapGet("/status", () => Results.Ok());
app.Run();
