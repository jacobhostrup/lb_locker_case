using System.Security.Cryptography;

namespace LBLocker.Infrastructure.Security;

public sealed class PinGenerator
{
    private const string Digits = "0123456789";

    public string Generate(int length = 6)
    {
        if (length is < 4 or > 12)
            throw new ArgumentOutOfRangeException(nameof(length), "PIN length must be between 4 and 12 digits.");

        return string.Create(length, 0, static (span, _) =>
        {
            for (var index = 0; index < span.Length; index++)
                span[index] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        });
    }
}
