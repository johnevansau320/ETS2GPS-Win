global using System.IO;
global using System.Net.Http;

namespace Ets2Gps
{
    /// <summary>MathF has no Hypot.</summary>
    public static class M
    {
        public static float Hypot(float a, float b) => MathF.Sqrt(a * a + b * b);
    }
}
