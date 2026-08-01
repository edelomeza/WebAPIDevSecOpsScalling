using FluentAssertions;
using Microsoft.Extensions.Options;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Services;

namespace UnitTest.Services
{
    public class PasswordHasherServiceTests
    {
        private static PasswordHasherService CreateService()
        {
            var options = Options.Create(new PasswordHasherOptions
            {
                SaltSize = 16,
                MemorySize = 65536,
                Iterations = 3,
                DegreeOfParallelism = 1
            });
            return new PasswordHasherService(options);
        }

        [Fact]
        public void VerifyPassword_HashVacio_ReturnsFalse()
        {
            var service = CreateService();
            service.VerifyPassword("pass", "   ").Should().BeFalse();
        }

        [Fact]
        public void VerifyPassword_FormatoDesconocido_ReturnsFalse()
        {
            var service = CreateService();
            service.VerifyPassword("pass", "$unknown$hash").Should().BeFalse();
        }

        [Fact]
        public void VerifyPassword_HashBcrypt_ReturnsTrue()
        {
            var service = CreateService();
            var hash = BCrypt.Net.BCrypt.HashPassword("12345678");
            service.VerifyPassword("12345678", hash).Should().BeTrue();
        }

        [Fact]
        public void VerifyPassword_HashArgon2idValido_ReturnsTrue()
        {
            var service = CreateService();
            var hash = service.HashPassword("12345678");
            service.VerifyPassword("12345678", hash).Should().BeTrue();
        }

        [Fact]
        public void VerifyPassword_HashArgon2idMalformado_ReturnsFalse()
        {
            var service = CreateService();
            service.VerifyPassword("pass", "$argon2id$v=19$m=65536,t=3$c2FsdA$aGFzaA==").Should().BeFalse();
        }

        [Fact]
        public void VerifyPassword_HashArgon2idParametrosParciales_ReturnsFalse()
        {
            var service = CreateService();
            service.VerifyPassword("pass", "$argon2id$v=19$m=65536,extra$c2FsdA$aGFzaA==").Should().BeFalse();
        }

        [Fact]
        public void VerifyPassword_HashArgon2idBase64Invalida_ReturnsFalse()
        {
            var service = CreateService();
            service.VerifyPassword("pass", "$argon2id$v=19$m=65536,t=3,p=1$!!!$###").Should().BeFalse();
        }

        [Fact]
        public void NeedsRehash_HashVacio_ReturnsTrue()
        {
            var service = CreateService();
            service.NeedsRehash("").Should().BeTrue();
        }

        [Fact]
        public void NeedsRehash_HashBcrypt_ReturnsTrue()
        {
            var service = CreateService();
            var hash = BCrypt.Net.BCrypt.HashPassword("12345678");
            service.NeedsRehash(hash).Should().BeTrue();
        }

        [Fact]
        public void NeedsRehash_Argon2idConParametrosActuales_ReturnsFalse()
        {
            var service = CreateService();
            var hash = service.HashPassword("12345678");
            service.NeedsRehash(hash).Should().BeFalse();
        }

        [Fact]
        public void NeedsRehash_Argon2idSinParametrosCompletos_ReturnsTrue()
        {
            var service = CreateService();
            service.NeedsRehash("$argon2id$v=19$c2FsdA==").Should().BeTrue();
        }

        [Fact]
        public void NeedsRehash_FormatoDesconocido_ReturnsTrue()
        {
            var service = CreateService();
            service.NeedsRehash("$desconocido$hash").Should().BeTrue();
        }
    }
}
