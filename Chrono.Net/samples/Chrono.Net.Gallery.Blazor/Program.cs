using Chrono.Net.Gallery.Web.Components;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
// Serve wwwroot of referenced libraries (Chrono.Net.Blazor: three.js viewport) in every environment when running from
// the build output; published apps get them copied into wwwroot anyway.
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 4 * 1024 * 1024);

var app = builder.Build();

// Fonts and the icon are copied from /assets into a "shared-assets" output folder and served from the site root.
// (Linking them under wwwroot would register them as static web assets whose source file does not exist.)
var sharedAssets = Path.Combine(AppContext.BaseDirectory, "shared-assets");
if (Directory.Exists(sharedAssets))
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(sharedAssets) });
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
