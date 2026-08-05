using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpAccess.SampleApi;

internal sealed record SampleLocalSettings(
    int Port,
    string JwtSigningKey,
    string TokenHashingKey,
    string PasswordPepper,
    string RateLimitPartitionKey,
    string AdminEmail,
    string AdminPassword,
    string ManagerEmail,
    string ManagerPassword,
    string UserEmail,
    string UserPassword,
    bool GoogleEnabled,
    string GoogleClientId,
    string GoogleClientSecret);

internal sealed record SampleBootstrapResult(string[] HostArguments, bool ResetSampleData);

internal static class SampleLocalSettingsBootstrap
{
    private const string CredentialTarget = "SharpAccess.SampleApi.LocalSettings";
    private const int MinimumAccountPasswordLength = 15;
    private const int MaximumAccountPasswordLength = 256;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static SampleBootstrapResult Prepare(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        bool resetLocalSetup = args.Contains("--reset-local-setup", StringComparer.OrdinalIgnoreCase);
        bool resetSampleData = args.Contains("--reset-sample-data", StringComparer.OrdinalIgnoreCase);
        string[] hostArguments = args.Where(static argument =>
            !string.Equals(argument, "--reset-local-setup", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(argument, "--reset-sample-data", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (IsAutomatedTestHost())
        {
            return new SampleBootstrapResult(hostArguments, resetSampleData);
        }

        if (resetLocalSetup)
        {
            WindowsCredentialStore.Delete(CredentialTarget);
        }

        string environmentName = Environment.GetEnvironmentVariable("APP_ENV")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";
        bool testEnvironment = string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
        bool skipInteractiveSetup = testEnvironment
            || string.Equals(Environment.GetEnvironmentVariable("SAMPLE_SKIP_SETUP"), "true", StringComparison.OrdinalIgnoreCase);

        SampleLocalSettings? settings = null;
        if (WindowsCredentialStore.TryRead(CredentialTarget, out string? serialized)
            && !string.IsNullOrWhiteSpace(serialized))
        {
            settings = JsonSerializer.Deserialize<SampleLocalSettings>(serialized, JsonOptions);
        }

        if (settings is not null && !HasValidAccountPasswords(settings))
        {
            if (skipInteractiveSetup)
            {
                throw new InvalidOperationException(
                    "Stored sample account passwords do not satisfy the SharpAccess password policy or account-derived password rule. Run interactively with --reset-local-setup.");
            }

            WindowsCredentialStore.Delete(CredentialTarget);
            settings = null;
            Console.WriteLine("Stored sample account passwords no longer satisfy the SharpAccess password policy or account-derived password rule.");
            Console.WriteLine("The local setup will run again so the credentials can be replaced.");
        }

        if (settings is null && !skipInteractiveSetup)
        {
            settings = CreateInteractively();
            WindowsCredentialStore.Write(CredentialTarget, JsonSerializer.Serialize(settings, JsonOptions));
            PrintAccountSummary(settings);
        }

        if (settings is not null)
        {
            Apply(settings);
        }

        EnsureRequiredRuntimeConfiguration(skipInteractiveSetup);
        if (resetSampleData)
        {
            Environment.SetEnvironmentVariable("SAMPLE_RESET_DATA", "true");
        }

        return new SampleBootstrapResult(hostArguments, resetSampleData);
    }

    private static bool IsAutomatedTestHost()
    {
        string entryAssembly = Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
        return entryAssembly.Contains("testhost", StringComparison.OrdinalIgnoreCase)
            || entryAssembly.Contains("vstest", StringComparison.OrdinalIgnoreCase)
            || AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    "Microsoft.AspNetCore.Mvc.Testing",
                    StringComparison.Ordinal));
    }

    private static SampleLocalSettings CreateInteractively()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The SharpAccess sample setup stores local secrets in Windows Credential Manager and must run on Windows.");
        }

        Console.WriteLine();
        Console.WriteLine("SharpAccess Sample API - first-run setup");
        Console.WriteLine("Secrets are stored for the current Windows user in Windows Credential Manager.");
        Console.WriteLine("Press Enter to accept a generated secret or the displayed default.");
        Console.WriteLine("Account passwords must contain 15 to 256 characters, including at least one letter and one digit.");
        Console.WriteLine("Account passwords must not contain the email name before @.");
        Console.WriteLine();

        int port = ReadPort();
        string adminEmail = ReadText("Administrator email", "admin@test.local");
        string adminPassword = ReadAccountPassword("Administrator password", adminEmail);
        string managerEmail = ReadText("Tenant manager email", "manager@test.local");
        string managerPassword = ReadAccountPassword("Tenant manager password", managerEmail);
        string userEmail = ReadText("Standard user email", "user@test.local");
        string userPassword = ReadAccountPassword("Standard user password", userEmail);

        Console.Write("Configure Google-compatible OpenID Connect now? [y/N]: ");
        bool googleEnabled = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
        string googleClientId = googleEnabled ? ReadText("Google client ID", string.Empty, allowEmpty: false) : string.Empty;
        string googleClientSecret = googleEnabled ? ReadSensitiveValue("Google client secret") : string.Empty;

        return new SampleLocalSettings(
            port,
            ReadSecret("JWT signing key", 32),
            ReadSecret("Refresh/token hashing key", 32),
            ReadSecret("Password pepper", 16),
            ReadSecret("Rate-limit partition key", 32),
            adminEmail,
            adminPassword,
            managerEmail,
            managerPassword,
            userEmail,
            userPassword,
            googleEnabled,
            googleClientId,
            googleClientSecret);
    }

    private static int ReadPort()
    {
        while (true)
        {
            string value = ReadText("HTTP port", "5000");
            if (int.TryParse(value, out int port) && port is >= 1 and <= 65535)
            {
                return port;
            }

            Console.WriteLine("Enter a port from 1 through 65535.");
        }
    }

    private static string ReadText(string prompt, string defaultValue, bool allowEmpty = false)
    {
        while (true)
        {
            Console.Write($"{prompt}{(string.IsNullOrEmpty(defaultValue) ? string.Empty : $" [{defaultValue}]")}: ");
            string value = Console.ReadLine()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(value))
            {
                value = defaultValue;
            }

            if (allowEmpty || !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine("A value is required.");
        }
    }

    private static string ReadAccountPassword(string prompt, string email)
    {
        while (true)
        {
            Console.Write($"{prompt} (blank generates one): ");
            string value = ReadMaskedValue();
            if (string.IsNullOrEmpty(value))
            {
                do
                {
                    value = GeneratePassword();
                }
                while (!IsValidAccountPassword(value, email));
                Console.WriteLine($"Generated: {value}");
            }

            if (IsValidAccountPassword(value, email))
            {
                return value;
            }

            Console.WriteLine(
                $"The password must contain {MinimumAccountPasswordLength} to {MaximumAccountPasswordLength} characters, including at least one letter and one digit, and must not contain the email name before @.");
        }
    }

    private static string ReadSensitiveValue(string prompt)
    {
        while (true)
        {
            Console.Write($"{prompt}: ");
            string value = ReadMaskedValue();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine("A value is required.");
        }
    }

    private static bool HasValidAccountPasswords(SampleLocalSettings settings) =>
        IsValidAccountPassword(settings.AdminPassword, settings.AdminEmail)
        && IsValidAccountPassword(settings.ManagerPassword, settings.ManagerEmail)
        && IsValidAccountPassword(settings.UserPassword, settings.UserEmail);

    private static bool IsValidAccountPassword(string? value, string? email)
    {
        if (value is null
            || value.Length < MinimumAccountPasswordLength
            || value.Length > MaximumAccountPasswordLength
            || !value.Any(char.IsLetter)
            || !value.Any(char.IsDigit))
        {
            return false;
        }

        string local = string.IsNullOrWhiteSpace(email)
            ? string.Empty
            : email.Trim().Split('@', 2)[0];
        return local.Length < 4
            || !value.Trim().Contains(local, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSecret(string prompt, int minimumBytes)
    {
        while (true)
        {
            Console.Write($"{prompt} (blank generates {minimumBytes} random bytes): ");
            string value = ReadMaskedValue();
            if (string.IsNullOrEmpty(value))
            {
                value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(minimumBytes));
                Console.WriteLine("Generated and stored.");
                return value;
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                decoded = Encoding.UTF8.GetBytes(value);
            }

            try
            {
                if (decoded.Length >= minimumBytes)
                {
                    return value;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decoded);
            }

            Console.WriteLine($"The value must contain at least {minimumBytes} bytes.");
        }
    }

    private static string ReadMaskedValue()
    {
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        StringBuilder value = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return value.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    private static string GeneratePassword()
    {
        string random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"Sa1!{random}";
    }

    private static void Apply(SampleLocalSettings settings)
    {
        SetWhenMissing("APP_ENV", "Development");
        SetWhenMissing("APP_PORT", settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetWhenMissing("APP_BASE_URL", $"http://localhost:{settings.Port}");
        SetWhenMissing("APP_JWT_KEY", settings.JwtSigningKey);
        SetWhenMissing("APP_REFRESH_TOKEN_HASH_KEY", settings.TokenHashingKey);
        SetWhenMissing("APP_PASSWORD_PEPPER", settings.PasswordPepper);
        SetWhenMissing("APP_RATE_LIMIT_PARTITION_KEY", settings.RateLimitPartitionKey);
        SetWhenMissing("APP_SEED_ADMIN", "true");
        SetWhenMissing("APP_SEED_ADMIN_EMAIL", settings.AdminEmail);
        SetWhenMissing("APP_SEED_ADMIN_PASSWORD", settings.AdminPassword);
        SetWhenMissing("SAMPLE_MANAGER_EMAIL", settings.ManagerEmail);
        SetWhenMissing("SAMPLE_MANAGER_PASSWORD", settings.ManagerPassword);
        SetWhenMissing("SAMPLE_USER_EMAIL", settings.UserEmail);
        SetWhenMissing("SAMPLE_USER_PASSWORD", settings.UserPassword);
        SetWhenMissing("SAMPLE_SEED_DEMO_DATA", "true");
        SetWhenMissing("Auth__ReturnRefreshTokenInResponseBody", "true");
        SetWhenMissing("OAUTH_GOOGLE_ENABLED", settings.GoogleEnabled ? "true" : "false");
        if (settings.GoogleEnabled)
        {
            SetWhenMissing("OAUTH_GOOGLE_CLIENT_ID", settings.GoogleClientId);
            SetWhenMissing("OAUTH_GOOGLE_CLIENT_SECRET", settings.GoogleClientSecret);
        }
    }

    private static void EnsureRequiredRuntimeConfiguration(bool nonInteractive)
    {
        string[] required =
        [
            "APP_JWT_KEY",
            "APP_REFRESH_TOKEN_HASH_KEY",
            "APP_PASSWORD_PEPPER",
            "APP_RATE_LIMIT_PARTITION_KEY"
        ];
        string[] missing = required.Where(static name =>
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        string mode = nonInteractive
            ? "Supply the values as environment variables for this non-interactive run."
            : "Run the first-use setup again with --reset-local-setup.";
        throw new InvalidOperationException($"Missing required sample configuration: {string.Join(", ", missing)}. {mode}");
    }

    private static void SetWhenMissing(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static void PrintAccountSummary(SampleLocalSettings settings)
    {
        Console.WriteLine();
        Console.WriteLine("Sample setup saved. Test accounts:");
        Console.WriteLine($"  Administrator: {settings.AdminEmail} / {settings.AdminPassword}");
        Console.WriteLine($"  Tenant manager: {settings.ManagerEmail} / {settings.ManagerPassword}");
        Console.WriteLine($"  Standard user:  {settings.UserEmail} / {settings.UserPassword}");
        Console.WriteLine("Use --reset-local-setup to replace stored settings.");
        Console.WriteLine("Use --reset-sample-data to recreate the local SQLite database.");
        Console.WriteLine();
    }

    private static class WindowsCredentialStore
    {
        private const uint CredentialTypeGeneric = 1;
        private const uint CredentialPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        internal static bool TryRead(string target, out string? value)
        {
            value = null;
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            if (!CredRead(target, CredentialTypeGeneric, 0, out IntPtr pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound)
                {
                    return false;
                }

                throw new Win32Exception(error, "Windows Credential Manager could not read the SharpAccess sample settings.");
            }

            try
            {
                NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                {
                    return false;
                }

                int blobSize = checked((int)credential.CredentialBlobSize);
                byte[] bytes = new byte[blobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                try
                {
                    value = Encoding.UTF8.GetString(bytes);
                    return true;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            finally
            {
                CredFree(pointer);
            }
        }

        internal static void Write(string target, string value)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Windows Credential Manager is required for sample secret storage.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            IntPtr blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                NativeCredential credential = new()
                {
                    Type = CredentialTypeGeneric,
                    TargetName = target,
                    Comment = "SharpAccess SampleApi local testing configuration",
                    CredentialBlobSize = checked((uint)bytes.Length),
                    CredentialBlob = blob,
                    Persist = CredentialPersistLocalMachine,
                    UserName = Environment.UserName
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows Credential Manager could not store the SharpAccess sample settings.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                Marshal.FreeCoTaskMem(blob);
            }
        }

        internal static void Delete(string target)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            if (!CredDelete(target, CredentialTypeGeneric, 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorNotFound)
                {
                    throw new Win32Exception(error, "Windows Credential Manager could not delete the SharpAccess sample settings.");
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(
            string target,
            uint type,
            uint flags,
            out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);
    }
}
