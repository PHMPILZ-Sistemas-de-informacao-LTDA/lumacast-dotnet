using LumaCast.Endpoints;
using LumaCast.Infrastructure;
using LumaCast.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLumaCastServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseLumaCastSecurityHeaders();
app.UseRouting();
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.MapStaticAssets().ShortCircuit();
app.MapHealthChecks("/healthz");
app.MapLiveKitEndpoints();
app.MapPeerToPeerSignaling();
app.MapRazorPages();

app.Run();

public partial class Program;
