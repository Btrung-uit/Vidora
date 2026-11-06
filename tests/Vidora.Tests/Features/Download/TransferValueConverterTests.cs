using System.Globalization;
using Vidora.Features.Download;

namespace Vidora.Tests.Features.Download;

[TestClass]
public sealed class TransferValueConverterTests
{
    [TestMethod]
    [DataRow(0L, "0 B")]
    [DataRow(999L, "999 B")]
    [DataRow(1000L, "1 KB")]
    [DataRow(45150547L, "45,2 MB")]
    public void Bytes_UsesReadableDecimalUnits(long bytes, string expected) =>
        Assert.AreEqual(expected, Convert(bytes, "Bytes"));

    [TestMethod]
    public void SpeedAndUnknownValues_HaveUnitsAndExplicitFallback()
    {
        Assert.AreEqual("11,5 MB/s", Convert(11464384.82003842, "Speed"));
        Assert.AreEqual("—", Convert(null, "Bytes"));
        Assert.AreEqual("—", Convert(double.NaN, "Speed"));
        Assert.AreEqual("—", Convert(-1L, "Bytes"));
        Assert.AreEqual("—", Convert(null, "Duration"));
    }

    [TestMethod]
    public void Duration_RemovesFractionsAndPreservesHoursBeyondOneDay()
    {
        Assert.AreEqual("00:03:51", Convert(TimeSpan.FromSeconds(231.185), "Duration"));
        Assert.AreEqual("25:02:03", Convert(new TimeSpan(1, 1, 2, 3), "Duration"));
    }

    private static object Convert(object? value, string parameter) => new TransferValueConverter().Convert(
        value!, typeof(string), parameter, CultureInfo.GetCultureInfo("vi-VN"));
}
