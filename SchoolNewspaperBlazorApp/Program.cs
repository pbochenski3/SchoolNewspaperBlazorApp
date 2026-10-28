using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SchoolNewspaperBlazorApp.Components;
using SchoolNewspaperBlazorApp.Data;
using SchoolNewspaperBlazorApp.Interfaces.Repository;
using SchoolNewspaperBlazorApp.Interfaces.Service;
using SchoolNewspaperBlazorApp.Repository;
using SchoolNewspaperBlazorApp.Service;

namespace SchoolNewspaperBlazorApp
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Pomocnik do ręcznego założenia konta (np. Master w SQL):
            //   dotnet run -- --hash TwojeHaslo
            // wypisuje skrót hasła i kończy działanie. Zastępuje wypisywanie skrótu "admin123" przy każdym starcie.
            if (args.Length == 2 && args[0] == "--hash")
            {
                Console.WriteLine(new PasswordHasher<string>().HashPassword(null!, args[1]));
                return;
            }

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            //sql connect
            builder.Services.AddDbContextFactory<NewspaperDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("NewspaperConnectionString")));

            //Repositry
            builder.Services.AddScoped<IArticleRepository, ArticleRepository>();
            builder.Services.AddScoped<IAuthRepository, AuthRepository>();
            builder.Services.AddScoped<IFileRepository, FileRepository>();
            builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
            builder.Services.AddScoped<IRoleRepository, RoleRepository>();
            builder.Services.AddScoped<IDictionaryRepository, DictionaryRepository>();
            builder.Services.AddScoped<ISubstitutionRepository, SubstitutionRepository>();


            //Services
            builder.Services.AddScoped<IArticleService, ArticleService>();
            builder.Services.AddScoped<IFileService, FileService>();
            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<IPermissionService, PermissionService>();
            builder.Services.AddScoped<IRoleService, RoleService>();
            builder.Services.AddScoped<ToastService>();
            builder.Services.AddScoped<IDictionaryService, DictionaryService>();
            builder.Services.AddScoped<ISubstitutionService, SubstitutionService>();

            builder.Services.AddAuthentication(
                CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.Cookie.Name = "SchoolNewspaper.Auth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = TimeSpan.FromHours(1);
                    options.SlidingExpiration = true;

                    // Konto usunięte przez admina przestaje działać od razu, a nie dopiero po wygaśnięciu ciasteczka
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        var idValue = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        var authRepository = context.HttpContext.RequestServices.GetRequiredService<IAuthRepository>();

                        User? user = null;
                        if (int.TryParse(idValue, out var userId))
                        {
                            user = await authRepository.GetUserByIdAsync(userId);
                        }

                        if (user == null)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    };
                });
            builder.Services.AddAuthorization();
            builder.Services.AddCascadingAuthenticationState();

            // Ograniczenie prób logowania / zmiany hasła (ochrona przed zgadywaniem haseł).
            // Uwaga: za reverse proxy trzeba włączyć UseForwardedHeaders, inaczej wszyscy będą mieć IP proxy.
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("login", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
            });

            var app = builder.Build();
            using (var scope = app.Services.CreateScope())
            {
                var contextFactory = scope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<NewspaperDbContext>>();

                await PermissionSeeder.SeedAsync(contextFactory);
            }
            var isDevelopment = app.Environment.IsDevelopment();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);

                app.UseHsts();
            }

            // Nagłówki bezpieczeństwa (clickjacking, sniffing typu pliku, wycieki adresu w Referer)
            app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

                // CSP tylko poza Development – w trybie deweloperskim narzędzia (hot reload) wstrzykują własne skrypty.
                // Gdyby po wdrożeniu coś przestało się ładować, to pierwsza linia do poluzowania/usunięcia.
                if (!isDevelopment)
                {
                    headers["Content-Security-Policy"] =
                        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
                        "script-src 'self'; connect-src 'self' ws: wss:; font-src 'self' data:; " +
                        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
                }

                await next();
            });

            app.MapPost("/auth/login", async (
                HttpContext context,
                IAuthService authService) =>
            {
                if (!context.Request.HasFormContentType)
                {
                    return Results.Redirect("/login");
                }

                var form = await context.Request.ReadFormAsync();
                string email = form["Email"].ToString();
                string password = form["Password"].ToString();
                int userId =  await authService.CheckIfRegisteredAsync(email, password);
                if (userId == 0)
                {
                    // wracamy na stronę logowania z komunikatem (wcześniej użytkownik lądował na stronie głównej bez żadnej informacji)
                    return Results.Redirect("/login?error=1");
                }

                // imię i nazwisko pokazujemy w nagłówku obok "Wyloguj się"
                var fullName = await authService.GetFullNameAsync(userId);

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name,email)
                };
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    claims.Add(new Claim("FullName", fullName));
                }

                // 1. logowanie (albo reset hasła przez admina) – najpierw trzeba ustawić własne hasło
                var mustChangePassword = await authService.MustChangePasswordAsync(userId);
                if (mustChangePassword)
                {
                    claims.Add(new Claim("MustChangePassword", "true"));
                }

                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    principal);
                return Results.Redirect(mustChangePassword ? "/zmiana-hasla" : "/adminPanel");
            }).RequireRateLimiting("login");

            // zmiana własnego hasła (formularz ze strony /zmiana-hasla)
            app.MapPost("/auth/change-password", async (
                HttpContext context,
                IAuthService authService) =>
            {
                if (context.User.Identity?.IsAuthenticated != true
                    || !int.TryParse(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                {
                    return Results.Redirect("/login");
                }

                if (!context.Request.HasFormContentType)
                {
                    return Results.Redirect("/zmiana-hasla");
                }

                var form = await context.Request.ReadFormAsync();
                string currentPassword = form["CurrentPassword"].ToString();
                string newPassword = form["NewPassword"].ToString();
                string confirmPassword = form["ConfirmPassword"].ToString();

                if (newPassword != confirmPassword)
                {
                    return Results.Redirect("/zmiana-hasla?error=" + Uri.EscapeDataString("Nowe hasła nie są takie same."));
                }

                try
                {
                    await authService.ChangePasswordAsync(userId, currentPassword, newPassword);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Redirect("/zmiana-hasla?error=" + Uri.EscapeDataString(ex.Message));
                }
                catch (Exception)
                {
                    return Results.Redirect("/zmiana-hasla?error=" + Uri.EscapeDataString("Nie udało się zmienić hasła."));
                }

                // nowe ciasteczko bez znacznika MustChangePassword, inaczej middleware dalej blokowałby użytkownika
                var claims = context.User.Claims.Where(c => c.Type != "MustChangePassword").ToList();
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity));
                return Results.Redirect("/adminPanel");
            }).RequireRateLimiting("login");

            //wylogowanie sie
            app.MapPost("/auth/logout", async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect("/");
            });

            app.UseHttpsRedirection();

            // zdjęcia wgrywane przez panel trafiają do wwwroot/Images
            app.UseStaticFiles();

            // starsze zdjęcia leżące w folderze Images obok projektu (poza wwwroot)
            var legacyImagesPath = Path.Combine(
                builder.Environment.ContentRootPath,
                "Images"
            );
            if (Directory.Exists(legacyImagesPath))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(legacyImagesPath),
                    RequestPath = "/Images"
                });
            }

            // Kto musi zmienić hasło, widzi tylko stronę zmiany hasła (nie wejdzie ręcznie np. na /adminPanel)
            app.UseAuthentication();
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path;
                var mustChange = context.User.Identity?.IsAuthenticated == true
                                 && context.User.HasClaim("MustChangePassword", "true");
                var allowed = path.StartsWithSegments("/zmiana-hasla")
                              || path.StartsWithSegments("/auth")
                              || path.StartsWithSegments("/_blazor")
                              || path.StartsWithSegments("/_framework")
                              || path.StartsWithSegments("/_content");

                if (mustChange && !allowed)
                {
                    context.Response.Redirect("/zmiana-hasla");
                    return;
                }

                await next();
            });

            // musi być po UseAuthentication, inaczej [Authorize] widzi użytkownika jako niezalogowanego
            app.UseAuthorization();
            app.UseRateLimiter();
            app.UseAntiforgery();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
