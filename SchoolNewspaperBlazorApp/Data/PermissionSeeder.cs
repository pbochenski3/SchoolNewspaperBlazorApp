
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SchoolNewspaperBlazorApp.Data;

public static class PermissionSeeder
{
    public static async Task SeedAsync(
        IDbContextFactory<NewspaperDbContext> contextFactory)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        if (!await context.Permissions.AnyAsync())
        {
            var permissions = Enum.GetValues<EnumPermissions>()
                .Select(permission => new Permission
                {
                    PermissionsName = permission,
                    PermissionViewName = permission switch
                    {
                        EnumPermissions.AddArticle => "Dodawanie artykułów",
                        EnumPermissions.EditArticle => "Edycja artykułów",
                        EnumPermissions.DeleteArticle => "Usuwanie artykułów",
                        EnumPermissions.AddUser => "Dodawanie użytkowników",
                        EnumPermissions.ManageUser => "Zarządzanie użytkownikami",
                        EnumPermissions.Master => "Administrator",
                        EnumPermissions.ManageClasses => "Zarządzanie klasami",
                        EnumPermissions.ManageTeachers => "Zarządzanie nauczycielami",
                        EnumPermissions.ManageRooms => "Zarządzanie salami",
                        EnumPermissions.ManageSubjects => "Zarządzanie przedmiotami",
                        EnumPermissions.AddSubstitution => "Dodawanie zastępstw",
                        EnumPermissions.ManageSubstitution => "Zarządzanie zastępstwami",
                        _ => permission.ToString()
                    }
                })
                .ToList();

            await context.Permissions.AddRangeAsync(permissions);
            await context.SaveChangesAsync();
        }

        var adminExists = await context.User
            .AnyAsync(user => user.Email == "admin@admin.pl");

        if (!adminExists)
        {
            var passwordHasher = new PasswordHasher<string>();

            var admin = new User
            {
                FirstName = "Admin",
                LastName = "Admin",
                Login = "admin",
                Email = "admin@admin.pl",
                Password = passwordHasher.HashPassword("admin", "admin"),
                RegisterDate = DateTime.Now,
                IsDeleted = false,
                MustChangePassword = true
            };

            context.User.Add(admin);
            await context.SaveChangesAsync();
        }
    }
}
