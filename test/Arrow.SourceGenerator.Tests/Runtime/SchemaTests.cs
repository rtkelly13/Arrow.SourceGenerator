using Apache.Arrow;
using Apache.Arrow.Types;
using Arrow.SourceGenerator.Tests.Models;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests.Runtime;

/// <summary>
/// The generated schema, exercised as compiled code: names, order, types, type parameters and
/// nullability (docs/02-TYPE-MAPPING.md is the contract these assert).
/// </summary>
public sealed class SchemaTests
{
    private static Field F(string name) => ScalarRowArrow.Schema.GetFieldByName(name);

    [Fact]
    public void OneFieldPerMemberInDeclarationOrder()
    {
        ScalarRowArrow
            .Schema.FieldsList.Select(f => f.Name)
            .Take(4)
            .ShouldBe(["Flag", "I8", "U8", "I16"]);
        ScalarRowArrow.Schema.FieldsList.Count.ShouldBe(35);
    }

    [Theory]
    [InlineData("Flag", ArrowTypeId.Boolean)]
    [InlineData("I8", ArrowTypeId.Int8)]
    [InlineData("U8", ArrowTypeId.UInt8)]
    [InlineData("I16", ArrowTypeId.Int16)]
    [InlineData("U16", ArrowTypeId.UInt16)]
    [InlineData("I32", ArrowTypeId.Int32)]
    [InlineData("U32", ArrowTypeId.UInt32)]
    [InlineData("I64", ArrowTypeId.Int64)]
    [InlineData("U64", ArrowTypeId.UInt64)]
    [InlineData("F32", ArrowTypeId.Float)]
    [InlineData("F64", ArrowTypeId.Double)]
    [InlineData("Text", ArrowTypeId.String)]
    [InlineData("Bytes", ArrowTypeId.Binary)]
    [InlineData("Money", ArrowTypeId.Decimal128)]
    [InlineData("Day", ArrowTypeId.Date32)]
    [InlineData("Time", ArrowTypeId.Time64)]
    [InlineData("WallClock", ArrowTypeId.Timestamp)]
    [InlineData("Instant", ArrowTypeId.Timestamp)]
    [InlineData("Elapsed", ArrowTypeId.Duration)]
    [InlineData("Id", ArrowTypeId.FixedSizedBinary)]
    [InlineData("Level", ArrowTypeId.Int16)]
    public void BuiltInsMapToTheirArrowTypes(string field, ArrowTypeId expected)
    {
        F(field).DataType.TypeId.ShouldBe(expected);

        // The nullable twin, where the model has one, maps to the same type.
        string twin = "Maybe" + field;
        if (ScalarRowArrow.Schema.FieldsList.Any(f => f.Name == twin))
        {
            F(twin).DataType.TypeId.ShouldBe(expected);
        }
    }

    [Fact]
    public void NullabilityFollowsTheModel()
    {
        F("I32").IsNullable.ShouldBeFalse();
        F("MaybeI32").IsNullable.ShouldBeTrue();
        F("Text").IsNullable.ShouldBeFalse();
        F("MaybeText").IsNullable.ShouldBeTrue();
        F("MaybeLevel").IsNullable.ShouldBeTrue();
    }

    [Fact]
    public void TypeParametersAreExact()
    {
        var money = (Decimal128Type)F("Money").DataType;
        (money.Precision, money.Scale).ShouldBe((38, 18));
        var price = (Decimal128Type)F("Price").DataType;
        (price.Precision, price.Scale).ShouldBe((10, 2));

        ((Time64Type)F("Time").DataType).Unit.ShouldBe(TimeUnit.Microsecond);
        var wall = (TimestampType)F("WallClock").DataType;
        wall.Unit.ShouldBe(TimeUnit.Microsecond);
        string.IsNullOrEmpty(wall.Timezone).ShouldBeTrue();
        var instant = (TimestampType)F("Instant").DataType;
        instant.Unit.ShouldBe(TimeUnit.Microsecond);
        instant.Timezone.ShouldBe("UTC");
        ((DurationType)F("Elapsed").DataType).Unit.ShouldBe(TimeUnit.Microsecond);
        ((FixedSizeBinaryType)F("Id").DataType).ByteWidth.ShouldBe(16);
    }

    [Fact]
    public void SchemaIsCachedAndUsableWithZeroRows()
    {
        ReferenceEquals(ScalarRowArrow.Schema, ScalarRowArrow.Schema).ShouldBeTrue();
        OrderArrow
            .Schema.FieldsList.Select(f => (f.Name, f.IsNullable))
            .ShouldBe([("Id", false), ("Customer", true), ("Total", false)]);
    }
}
