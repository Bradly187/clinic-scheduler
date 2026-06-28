using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClinicScheduler.Infrastructure.Data.Encryption;

/// <summary>
/// A value converter that encrypts string values before writing them to the database
/// and decrypts them when reading from the database using ASP.NET Core Data Protection.
/// </summary>
public class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter(IDataProtector protector, ConverterMappingHints? mappingHints = null)
        : base(
            v => v == null ? null : protector.Protect(v),
            v => v == null ? null : protector.Unprotect(v),
            mappingHints)
    {
    }
}
