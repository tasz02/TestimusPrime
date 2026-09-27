var app = WebApplication.CreateBuilder(args).Build();
app.MapGet("/status", () => Results.Ok());
app.Run();
