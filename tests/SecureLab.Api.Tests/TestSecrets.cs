namespace SecureLab.Api.Tests;

internal static class TestSecrets
{
    // Пароль seed-користувачів береться лише зі змінної середовища; у файлах і Git його немає.
    public static string SeedPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    // traceId унікальний для кожного запиту, тому при порівнянні тіл problem+json його прибираємо.
    public static string WithoutTraceId(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"");

    // Одноразовий складний пароль для тестів реєстрації; не відповідає жодному реальному обліковому запису.
    public static string NewStrongPassword() => "T3s7P@55w0Rd!" + Guid.NewGuid().ToString("N");
}
