using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace SampleManager
{
    internal enum AuthRole
    {
        None,
        QA,
        SampleManagement
    }

    internal sealed class AuthenticationService
    {
        private const int Pbkdf2Iterations = 100000;
        private const int DerivedKeyLength = 32;

        private static readonly byte[] QaSalt = Convert.FromBase64String("9j/ZSXMlZVOWeYgRpvYo+w==");
        private static readonly byte[] QaHash = Convert.FromBase64String("LJIvnh/pyD9kCaykk0Lf4ijgbEioftw1ffz88Y6wVFw=");
        private static readonly byte[] SampleManagementSalt = Convert.FromBase64String("6vdG1slABElQQal49HKAjw==");
        private static readonly byte[] SampleManagementHash = Convert.FromBase64String("sn1AAlvDVsNmZRn8Ls4qIjcqal6jO+oTw9JL1GMDqhw=");

        private readonly string stateFilePath;

        internal AuthenticationService()
        {
            stateFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CPC",
                "Sample Manager",
                "auth-state.dat");
            CurrentRole = LoadRememberedRole();
        }

        internal AuthRole CurrentRole { get; private set; }

        internal string StateFilePath
        {
            get { return stateFilePath; }
        }

        internal bool RequireRole(IWin32Window owner, AuthRole role)
        {
            if (role == AuthRole.None)
            {
                return false;
            }

            if (CurrentRole == role)
            {
                return true;
            }

            bool authenticated;
            using (PasswordDialog dialog = new PasswordDialog(GetRoleTitle(role)))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }

                authenticated = VerifyPassword(role, dialog.Password);
            }

            if (!authenticated)
            {
                MessageBox.Show(
                    owner,
                    "Sai mật khẩu",
                    GetRoleTitle(role),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            CurrentRole = role;
            SaveRememberedRole(role);
            return true;
        }

        internal void Logout()
        {
            CurrentRole = AuthRole.None;
            try
            {
                if (File.Exists(stateFilePath))
                {
                    File.Delete(stateFilePath);
                }
            }
            catch
            {
            }
        }

        internal static string GetRoleTitle(AuthRole role)
        {
            return role == AuthRole.QA ? "Yêu cầu mẫu" : "Quản lý mẫu";
        }

        internal static bool VerifyPassword(AuthRole role, string password)
        {
            if (String.IsNullOrEmpty(password))
            {
                return false;
            }

            byte[] salt;
            byte[] expectedHash;
            if (role == AuthRole.QA)
            {
                salt = QaSalt;
                expectedHash = QaHash;
            }
            else if (role == AuthRole.SampleManagement)
            {
                salt = SampleManagementSalt;
                expectedHash = SampleManagementHash;
            }
            else
            {
                return false;
            }

            try
            {
                using (Rfc2898DeriveBytes deriveBytes = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations))
                {
                    byte[] actualHash = deriveBytes.GetBytes(DerivedKeyLength);
                    return FixedTimeEquals(expectedHash, actualHash);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private AuthRole LoadRememberedRole()
        {
            try
            {
                if (!File.Exists(stateFilePath))
                {
                    return AuthRole.None;
                }

                byte[] protectedState = File.ReadAllBytes(stateFilePath);
                byte[] state = ProtectedData.Unprotect(
                    protectedState,
                    null,
                    DataProtectionScope.CurrentUser);
                string roleCode = Encoding.UTF8.GetString(state);
                return ParseRole(roleCode);
            }
            catch
            {
                return AuthRole.None;
            }
        }

        private void SaveRememberedRole(AuthRole role)
        {
            try
            {
                string directory = Path.GetDirectoryName(stateFilePath);
                Directory.CreateDirectory(directory);
                byte[] state = Encoding.UTF8.GetBytes(GetRoleCode(role));
                byte[] protectedState = ProtectedData.Protect(
                    state,
                    null,
                    DataProtectionScope.CurrentUser);
                File.WriteAllBytes(stateFilePath, protectedState);
            }
            catch
            {
            }
        }

        private static AuthRole ParseRole(string roleCode)
        {
            if (String.Equals(roleCode, "QA", StringComparison.Ordinal))
            {
                return AuthRole.QA;
            }

            if (String.Equals(roleCode, "SAMPLE_MANAGEMENT", StringComparison.Ordinal))
            {
                return AuthRole.SampleManagement;
            }

            return AuthRole.None;
        }

        private static string GetRoleCode(AuthRole role)
        {
            return role == AuthRole.QA ? "QA" : "SAMPLE_MANAGEMENT";
        }

        private static bool FixedTimeEquals(byte[] expected, byte[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < expected.Length; index++)
            {
                difference |= expected[index] ^ actual[index];
            }

            return difference == 0;
        }
    }
}
