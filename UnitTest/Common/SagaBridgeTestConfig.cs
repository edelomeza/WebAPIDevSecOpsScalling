using Microsoft.Extensions.Configuration;

namespace UnitTest
{
    /// <summary>
    /// PostDespliegue9: IConfiguration en memoria para el feature flag SagaBridge en tests.
    /// BridgeOff = comportamiento legacy exacto (sin dual-write, stock legacy).
    /// BridgeOn = puente activo (dual-write VenPedido, stock solo-saga).
    /// En namespace raíz UnitTest para ser visible sin usings desde UnitTest.Venta, etc.
    /// </summary>
    public static class SagaBridgeTestConfig
    {
        public static IConfiguration BridgeOff() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Feature:SagaBridge"] = "false" })
            .Build();

        public static IConfiguration BridgeOn() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Feature:SagaBridge"] = "true" })
            .Build();
    }
}
