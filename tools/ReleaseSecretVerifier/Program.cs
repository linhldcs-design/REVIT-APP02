using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

if (args.Length != 1)
    return Fail("Usage: ReleaseSecretVerifier <RevitAPP.Licensing.dll>");

var expected = Environment.GetEnvironmentVariable("REVITAPP_LICENSE_SHARED_SECRET");
if (!IsValidSecret(expected))
    return Fail("REVITAPP_LICENSE_SHARED_SECRET is not a valid 64-character Base64URL token.");

var embedded = ReadEmbeddedSecret(Path.GetFullPath(args[0]));
if (embedded is null)
    return Fail("LicenseConfig.SharedSecret was not found in the built DLL.");

var expectedBytes = Encoding.UTF8.GetBytes(expected!);
var embeddedBytes = Encoding.UTF8.GetBytes(embedded);
if (!CryptographicOperations.FixedTimeEquals(expectedBytes, embeddedBytes))
    return Fail($"Embedded release secret mismatch (embedded length: {embedded.Length}).");

Console.WriteLine($"Embedded release secret verified (length: {embedded.Length}).");
return 0;

static bool IsValidSecret(string? value) =>
    value is { Length: 64 } && value.All(character =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');

static string? ReadEmbeddedSecret(string path)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();

    foreach (var typeHandle in metadata.TypeDefinitions)
    {
        var type = metadata.GetTypeDefinition(typeHandle);
        if (metadata.GetString(type.Namespace) != "RevitAPP.Licensing" ||
            metadata.GetString(type.Name) != "LicenseConfig") continue;

        foreach (var methodHandle in type.GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            if (metadata.GetString(method.Name) != "get_SharedSecret") continue;

            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            if (il is null) continue;
            for (var index = 0; index + sizeof(int) < il.Length; index++)
            {
                if (il[index] != 0x72) continue; // ldstr
                var token = BitConverter.ToInt32(il, index + 1);
                return metadata.GetUserString(MetadataTokens.UserStringHandle(token));
            }
        }
    }

    return null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
