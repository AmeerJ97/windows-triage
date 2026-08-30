using WindowsTriage.Core.Collectors;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class PowerReportParserTests
{
    [Fact]
    public void ParseBattery_HandlesNamespacesAndCapacityNames()
    {
        const string xml = "<Report xmlns='urn:test'><Battery><DesignCapacity>50000 mWh</DesignCapacity><FullChargeCapacity>37500 mWh</FullChargeCapacity></Battery></Report>";
        var result = PowerReportParser.ParseBattery(xml);
        Assert.Equal((uint)50000, result.Design);
        Assert.Equal((uint)37500, result.Full);
    }

    [Fact]
    public void ParseEnergy_CountsErrorsAndWarnings()
    {
        const string xml = "<Energy><Errors><Error/><Error/></Errors><Warnings><Warning/></Warnings></Energy>";
        var result = PowerReportParser.ParseEnergy(xml);
        Assert.Equal(2, result.Errors);
        Assert.Equal(1, result.Warnings);
    }

    [Fact]
    public void ParseEnergy_RejectsMalformedXml() => Assert.ThrowsAny<Exception>(() => PowerReportParser.ParseEnergy("<broken>"));
}
