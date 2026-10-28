using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SchoolNewspaperBlazorApp.Data;
using SchoolNewspaperBlazorApp.Interfaces.Repository;
using SchoolNewspaperBlazorApp.Interfaces.Service;

namespace SchoolNewspaperBlazorApp.Service
{
    public class AuthService : IAuthService
    {
        private readonly IAuthRepository _authRepository;
        private readonly IPermissionRepository _permissionRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IPermissionService _permissionService;
        private readonly AuthenticationStateProvider _authenticationStateProvider;
        private readonly ILogger<AuthService> _logger;

        private const int MinPasswordLength = 8;
        private const int MaxPasswordLength = 128;
        private const int MaxLoginLength = 100;
        private const int MaxEmailLength = 254;
        private const int MaxNameLength = 100;

        // Skrót "na potrzeby porównania" – gdy konta nie ma, i tak liczymy hash, żeby czas odpowiedzi
        // nie zdradzał, czy taki login/e-mail istnieje w systemie.
        private static readonly string DummyHash =
            new PasswordHasher<string>().HashPassword(null!, Guid.NewGuid().ToString());

        public AuthService(
            IAuthRepository authRepository,
            IPermissionRepository permissionRepository,
            IRoleRepository roleRepository,
            IPermissionService permissionService,
            AuthenticationStateProvider authenticationStateProvider,
            ILogger<AuthService> logger)
        {
            _authRepository = authRepository;
            _permissionRepository = permissionRepository;
            _roleRepository = roleRepository;
            _permissionService = permissionService;
            _authenticationStateProvider = authenticationStateProvider;
            _logger = logger;
        }

        public async Task<Dictionary<int, string>> GetAllUsersAsync()
        {
            // lista kont z e-mailami jest tylko dla administracji
            await EnsureCanManageUsersAsync();
            return await _authRepository.GetAllUsersAsync();
        }

        public async Task AddUserAsync(string login, string password, string email, string firstName, string lastName)
        {
            // Tylko AddUser (albo Master) – wcześniej konto mógł założyć każdy, kto wywołał tę metodę
            //if (!await _permissionService.HasPermissionAsync(EnumPermissions.AddUser))
            //{
            //    throw new UnauthorizedAccessException();
            //}

            login = (login ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim();
            firstName = (firstName ?? string.Empty).Trim();
            lastName = (lastName ?? string.Empty).Trim();

            ValidateAccountFields(login, email, firstName, lastName);
            ValidatePassword(password);

            if (await _authRepository.IsLoginOrEmailTakenAsync(0, login, email))
            {
                throw new InvalidOperationException("Ten login lub e-mail jest już zajęty.");
            }

            var state = await _authenticationStateProvider.GetAuthenticationStateAsync();

            var hasher = new PasswordHasher<string>();
            string passwordHash = hasher.HashPassword(null!, password);
            var user = new User
            {
                Login = login,
                Password = passwordHash,
                Email = email,
                RegisterDate = DateTime.Now,
                FirstName = firstName,
                LastName = lastName,
                AddBy = state.User.Identity?.Name ?? string.Empty,
                // hasło ustawił admin, więc przy 1. logowaniu użytkownik ustawia własne
                MustChangePassword = true
            };

            await _authRepository.AddUserAsync(user);
        }

        // Zwraca Id użytkownika albo 0, gdy login/hasło się nie zgadzają
        public async Task<int> CheckIfRegisteredAsync(
            string login, string password)
        {
            // odrzucamy puste i absurdalnie długie dane, zanim zaczniemy liczyć hash
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password)
                || login.Length > MaxEmailLength || password.Length > 256)
            {
                return 0;
            }

            var hasher = new PasswordHasher<string>();

            User? user = null;
            try
            {
                user = await _authRepository.CheckIfRegisteredAsync(login.Trim(), password);
            }
            catch (Exception ex)
            {
                // brak konta (albo chwilowy błąd bazy) = nieudane logowanie
                _logger.LogDebug(ex, "Logowanie nieudane – nie znaleziono konta");
            }

            if (user == null)
            {
                hasher.VerifyHashedPassword(null!, DummyHash, password);
                return 0;
            }

            var result = hasher.VerifyHashedPassword(null!, user.Password, password);

            // SuccessRehashNeeded = hasło poprawne, tylko skrót został wyliczony starszymi ustawieniami
            return result == PasswordVerificationResult.Success
                   || result == PasswordVerificationResult.SuccessRehashNeeded
                ? user.Id
                : 0;
        }

        public async Task<string> GetFullNameAsync(int userId)
        {
            var user = await _authRepository.GetUserByIdAsync(userId);
            return user == null ? "" : $"{user.FirstName} {user.LastName}".Trim();
        }

        public async Task<UserDetails?> GetUserDetailsAsync(int id)
        {
            await EnsureCanManageUsersAsync();

            var user = await _authRepository.GetUserByIdAsync(id);
            return user == null
                ? null
                : new UserDetails(user.Id, user.Login, user.Email, user.FirstName, user.LastName);
        }

        public async Task UpdateUserAsync(int id, string login, string email, string firstName, string lastName, string? newPassword)
        {
            // 1. Tylko ManageUser lub Master – sprawdzamy to tutaj, a nie tylko w interfejsie
            await EnsureCanManageUsersAsync();

            login = (login ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim();
            firstName = (firstName ?? string.Empty).Trim();
            lastName = (lastName ?? string.Empty).Trim();

            ValidateAccountFields(login, email, firstName, lastName);

            if (!string.IsNullOrEmpty(newPassword))
            {
                ValidatePassword(newPassword);
            }

            // 2. Konto administratora (Master) może edytować tylko jego właściciel
            //    (inaczej ktoś z ManageUser mógłby zmienić hasło Mastera i się na niego zalogować)
            var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var currentId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (currentId != id.ToString())
            {
                var permissions = await _permissionRepository.GetUserPermissionsAsync(id);
                var role = await _roleRepository.GetRoleByUserIdAsync(id);
                if (permissions.Contains(EnumPermissions.Master) || Role.IsMasterName(role?.RoleName))
                {
                    throw new InvalidOperationException("Nie można edytować konta administratora.");
                }
            }

            // 3. Login i e-mail muszą być unikalne (po nich logujemy się do panelu)
            if (await _authRepository.IsLoginOrEmailTakenAsync(id, login, email))
            {
                throw new InvalidOperationException("Ten login lub e-mail jest już zajęty.");
            }

            // 4. Puste hasło = bez zmiany hasła
            string? passwordHash = null;
            if (!string.IsNullOrEmpty(newPassword))
            {
                passwordHash = new PasswordHasher<string>().HashPassword(null!, newPassword);
            }

            // Hasło ustawione komuś innemu = ta osoba musi je zmienić przy logowaniu.
            // Własne hasło zmieniamy świadomie, więc bez wymuszania.
            var forcePasswordChange = currentId != id.ToString();

            await _authRepository.UpdateUserAsync(id, login, email, firstName, lastName, passwordHash, forcePasswordChange);
        }

        public async Task<bool> MustChangePasswordAsync(int userId)
        {
            var user = await _authRepository.GetUserByIdAsync(userId);
            return user != null && user.MustChangePassword;
        }

        // userId pochodzi z ciasteczka logowania (nie z formularza), więc zmieniamy tylko własne hasło
        public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
        {
            ValidatePassword(newPassword);

            var user = await _authRepository.GetUserByIdAsync(userId);
            if (user == null)
            {
                throw new InvalidOperationException("Konto nie istnieje.");
            }

            var hasher = new PasswordHasher<string>();
            var verification = hasher.VerifyHashedPassword(null!, user.Password, currentPassword ?? string.Empty);
            if (verification == PasswordVerificationResult.Failed)
            {
                throw new InvalidOperationException("Obecne hasło jest nieprawidłowe.");
            }

            if (currentPassword == newPassword)
            {
                throw new InvalidOperationException("Nowe hasło musi się różnić od obecnego.");
            }

            var newHash = hasher.HashPassword(null!, newPassword);
            await _authRepository.UpdatePasswordAsync(userId, newHash);
        }

        public async Task DeleteUserByIdAsync(int id)
        {
            // 1. Tylko ManageUser lub Master – sprawdzamy to tutaj, a nie tylko w interfejsie
            await EnsureCanManageUsersAsync();

            // 2. Nie usuwamy własnego konta
            var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var currentId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (currentId == id.ToString())
            {
                throw new InvalidOperationException("Nie możesz usunąć własnego konta.");
            }

            // 3. Nie usuwamy konta administratora (Master)
            var permissions = await _permissionRepository.GetUserPermissionsAsync(id);
            var role = await _roleRepository.GetRoleByUserIdAsync(id);
            if (permissions.Contains(EnumPermissions.Master) || Role.IsMasterName(role?.RoleName))
            {
                throw new InvalidOperationException("Nie można usunąć konta administratora.");
            }

            await _authRepository.DeleteUserByIdAsync(id);
        }

        // ManageUser (Master ma wszystkie uprawnienia automatycznie)
        private async Task EnsureCanManageUsersAsync()
        {
            if (!await _permissionService.HasPermissionAsync(EnumPermissions.ManageUser))
            {
                throw new UnauthorizedAccessException();
            }
        }

        private static void ValidateAccountFields(string login, string email, string firstName, string lastName)
        {
            if (login.Length == 0 || email.Length == 0)
            {
                throw new InvalidOperationException("Login i e-mail są wymagane.");
            }

            if (login.Length > MaxLoginLength)
            {
                throw new InvalidOperationException($"Login może mieć maksymalnie {MaxLoginLength} znaków.");
            }

            if (!IsValidEmail(email))
            {
                throw new InvalidOperationException("Podaj poprawny adres e-mail.");
            }

            if (firstName.Length > MaxNameLength || lastName.Length > MaxNameLength)
            {
                throw new InvalidOperationException($"Imię i nazwisko mogą mieć maksymalnie {MaxNameLength} znaków.");
            }
        }

        private static void ValidatePassword(string? password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            {
                throw new InvalidOperationException($"Hasło musi mieć co najmniej {MinPasswordLength} znaków.");
            }

            if (password.Length > MaxPasswordLength)
            {
                throw new InvalidOperationException($"Hasło może mieć maksymalnie {MaxPasswordLength} znaków.");
            }
        }

        private static bool IsValidEmail(string email)
        {
            if (email.Length > MaxEmailLength)
            {
                return false;
            }

            try
            {
                return new MailAddress(email).Address == email;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
