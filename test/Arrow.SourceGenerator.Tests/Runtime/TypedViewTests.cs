using Apache.Arrow;
using Arrow.SourceGenerator.Tests.Models;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests.Runtime;

/// <summary>The typed view: validated once, zero-copy, and bound to the batch's lifetime.</summary>
public sealed class TypedViewTests
{
    [Fact]
    public void ViewExposesTheBatchArraysUnderMemberNamesWithoutCopying()
    {
        using RecordBatch batch = OrderArrow.ToRecordBatch([
            new Order(1, "a", 2.5m),
            new Order(2, null, 3m),
        ]);

        OrderArrowView view = OrderArrow.View(batch);

        view.Length.ShouldBe(2);
        ReferenceEquals(view.Batch, batch).ShouldBeTrue();
        ReferenceEquals(view.Id, batch.Column(0))
            .ShouldBeTrue("the view hands out the batch's own arrays");
        view.Id.Values.ToArray().ShouldBe([1L, 2L]);
        view.Customer.GetString(0).ShouldBe("a");
        view.Customer.IsNull(1).ShouldBeTrue();
        view.Total.GetValue(1).ShouldBe(3m);
    }

    [Fact]
    public void ViewResolvesFieldsByNameRegardlessOfOrder()
    {
        using RecordBatch original = OrderArrow.ToRecordBatch([new Order(7, "x", 1m)]);
        var reordered = new Schema.Builder()
            .Field(original.Schema.GetFieldByName("Total"))
            .Field(original.Schema.GetFieldByName("Id"))
            .Field(original.Schema.GetFieldByName("Customer"))
            .Build();
        using var batch = new RecordBatch(
            reordered,
            [original.Column(2), original.Column(0), original.Column(1)],
            1
        );

        OrderArrowView view = OrderArrow.View(batch);
        view.Id.GetValue(0).ShouldBe(7);
        view.Customer.GetString(0).ShouldBe("x");
    }

    [Fact]
    public void ViewValidatesExactlyLikeTheReader()
    {
        using var wrong = new RecordBatch(
            new Schema.Builder()
                .Field(f => f.Name("Id").DataType(Apache.Arrow.Types.Int32Type.Default))
                .Build(),
            [new Int32Array.Builder().Append(1).Build()],
            1
        );

        string viewError = Should.Throw<InvalidDataException>(() => OrderArrow.View(wrong)).Message;
        string readError = Should
            .Throw<InvalidDataException>(() => OrderArrow.FromRecordBatch(wrong))
            .Message;
        viewError.ShouldBe(readError);
        Should.Throw<ArgumentNullException>(() => OrderArrow.View(null!));
    }

    /// <summary>
    /// Regression: generated code lives in the consumer's assembly, so its internal constructor is
    /// callable there. It used to take the arrays as arguments, letting any code build a view over
    /// unchecked arrays; the only constructor now validates the batch itself.
    /// </summary>
    [Fact]
    public void TheViewConstructorValidatesToo()
    {
        using var wrong = new RecordBatch(
            new Schema.Builder()
                .Field(f => f.Name("Id").DataType(Apache.Arrow.Types.Int32Type.Default))
                .Build(),
            [new Int32Array.Builder().Append(1).Build()],
            1
        );

        Should
            .Throw<InvalidDataException>(() => new OrderArrowView(wrong))
            .Message.ShouldBe(
                Should.Throw<InvalidDataException>(() => OrderArrow.View(wrong)).Message
            );
        Should.Throw<ArgumentNullException>(() => new OrderArrowView(null!));

        using RecordBatch batch = OrderArrow.ToRecordBatch([new Order(1, "a", 2.5m)]);
        new OrderArrowView(batch).Id.GetValue(0).ShouldBe(1);
    }

    [Fact]
    public void EveryP0KindHasATypedArrayOnTheView()
    {
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([ToRecordBatchTests.Sample(4)]);
        ScalarRowArrowView view = ScalarRowArrow.View(batch);

        view.Instant.ShouldBeOfType<TimestampArray>();
        view.Id.ShouldBeOfType<Apache.Arrow.Arrays.FixedSizeBinaryArray>();
        view.Money.ShouldBeOfType<Decimal128Array>();
        view.Level.Values[0].ShouldBe((short)Level.Low);
    }
}
