using Apache.Arrow;
using Apache.Arrow.Types;
using Arrow.SourceGenerator.Tests.Models;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests.Runtime;

/// <summary>Adapted members, compiled by the real analyzer: storage, nulls and round trips.</summary>
public sealed class AdapterTests
{
    private static AdapterRow Sample(int seed) =>
        new()
        {
            Price = new Money(10.25m + seed),
            Discount = seed % 2 == 0 ? null : new Money(1.5m),
            Contact = new EmailAddress($"user{seed}@example.com"),
            Backup = seed % 2 == 0 ? null : new EmailAddress("backup@example.com"),
            Reading = new Temperature(-3.5 * seed),
            Seen = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000 + seed),
        };

    [Fact]
    public void AdaptedMembersMapThroughTheirSurrogateTypes()
    {
        Schema schema = AdapterRowArrow.Schema;

        var price = (Decimal128Type)schema.GetFieldByName("Price").DataType;
        (price.Precision, price.Scale).ShouldBe(
            (18, 2),
            "[ArrowDecimal] applies to a decimal surrogate"
        );
        schema.GetFieldByName("Discount").IsNullable.ShouldBeTrue();
        schema.GetFieldByName("Contact").DataType.TypeId.ShouldBe(ArrowTypeId.String);
        schema.GetFieldByName("Contact").IsNullable.ShouldBeFalse();
        schema.GetFieldByName("Reading").DataType.TypeId.ShouldBe(ArrowTypeId.Double);
        schema
            .GetFieldByName("Seen")
            .DataType.TypeId.ShouldBe(
                ArrowTypeId.Int64,
                "the explicit adapter overrides the built-in Timestamp"
            );
    }

    [Fact]
    public void StoredValuesAreTheAdaptersOutput()
    {
        using RecordBatch batch = AdapterRowArrow.ToRecordBatch([Sample(1)]);

        ((Decimal128Array)batch.Column(0)).GetValue(0).ShouldBe(11.25m);
        ((StringArray)batch.Column(2)).GetString(0).ShouldBe("user1@example.com");
        ((Int64Array)batch.Column(5)).GetValue(0).ShouldBe(1_700_000_001);
    }

    [Fact]
    public void AdaptedMembersRoundTripWithNullsHandledBeforeTheAdapter()
    {
        AdapterRow[] rows = [Sample(0), Sample(1), Sample(2)];
        using RecordBatch batch = IpcRoundTripTests.RoundTrip(AdapterRowArrow.ToRecordBatch(rows));

        AdapterRow[] read = AdapterRowArrow.FromRecordBatch(batch);

        for (int i = 0; i < rows.Length; i++)
        {
            read[i].Price.ShouldBe(rows[i].Price);
            read[i].Discount.ShouldBe(rows[i].Discount);
            read[i].Contact.ShouldBe(rows[i].Contact);
            read[i].Backup.ShouldBe(rows[i].Backup);
            read[i].Reading.ShouldBe(rows[i].Reading);
            read[i].Seen.ShouldBe(rows[i].Seen);
        }
    }

    [Fact]
    public void NullRequiredReferenceDomainIsRejectedWithoutCallingTheAdapter()
    {
        AdapterRow row = Sample(1);
        row.Contact = null!;

        Should
            .Throw<ArgumentException>(() => AdapterRowArrow.ToRecordBatch([row]))
            .Message.ShouldContain("non-nullable Arrow field 'Contact'");
    }

    [Fact]
    public void AdapterExceptionsOnReadPropagate()
    {
        var schema = AdapterRowArrow.Schema;
        using RecordBatch valid = AdapterRowArrow.ToRecordBatch([Sample(1)]);
        var badEmail = new StringArray.Builder().Append("not-an-email").Build();
        IArrowArray[] columns =
        [
            .. Enumerable
                .Range(0, valid.ColumnCount)
                .Select(i => i == 2 ? badEmail : valid.Column(i)),
        ];
        using var batch = new RecordBatch(schema, columns, 1);

        Should
            .Throw<ArgumentException>(() => AdapterRowArrow.FromRecordBatch(batch))
            .Message.ShouldContain("not an email");
    }

    [Fact]
    public void ViewExposesTheSurrogateArrays()
    {
        using RecordBatch batch = AdapterRowArrow.ToRecordBatch([Sample(3)]);
        AdapterRowArrowView view = AdapterRowArrow.View(batch);

        view.Contact.GetString(0).ShouldBe("user3@example.com");
        view.Seen.GetValue(0).ShouldBe(1_700_000_003);
    }
}
