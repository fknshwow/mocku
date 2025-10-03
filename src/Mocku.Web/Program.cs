using Mocku.Web.Components;
using Mocku.Web.Services;
using Mocku.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register our services
builder.Services.AddSingleton<MockApiService>();
builder.Services.AddSingleton<MockFileService>();
builder.Services.AddSingleton<TemplateProcessor>();
builder.Services.AddSingleton<RequestLogService>();
builder.Services.AddScoped<RequestGeneratorService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Add mock API middleware before static files and routing
app.UseMiddleware<MockApiMiddleware>();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
