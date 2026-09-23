# /// script
# requires-python = ">=3.11"
# dependencies = ["pyarrow==25.0.1"]
# ///
"""External interoperability gate: PyArrow checks Arrow.SourceGenerator's output and produces input
for it (docs/00-DESIGN-GOALS.md section 28).

    uv run scripts/pyarrow_interop.py verify FILE    # FILE written by the generated C# writer
    uv run scripts/pyarrow_interop.py produce FILE   # FILE for the generated C# reader

The schema and rows mirror test/Arrow.SourceGenerator.Interop/InteropRow.cs exactly; each half checks
the other, so a drift in either is a failure.
"""

import datetime as dt
import sys
import uuid
from decimal import Decimal

import pyarrow as pa
import pyarrow.ipc as ipc

UTC = dt.timezone.utc

# (name, type, nullable) as the generator declares them.
SCHEMA = [
    ("Id", pa.int64(), False),
    ("Count", pa.int32(), True),
    ("Score", pa.float64(), False),
    ("Flag", pa.bool_(), False),
    ("Name", pa.string(), False),
    ("Note", pa.string(), True),
    ("Payload", pa.binary(), False),
    ("Amount", pa.decimal128(18, 4), False),
    ("Day", pa.date32(), False),
    ("At", pa.time64("us"), False),
    ("Local", pa.timestamp("us"), False),
    ("Instant", pa.timestamp("us", tz="UTC"), False),
    ("Elapsed", pa.duration("us"), False),
    ("Key", pa.binary(16), False),
    ("Level", pa.uint8(), False),
    ("Big", pa.uint64(), False),
]

ROWS = [
    {
        "Id": 1,
        "Count": 42,
        "Score": 1.5,
        "Flag": True,
        "Name": "ascii",
        "Note": "note",
        "Payload": bytes([0, 1, 255]),
        "Amount": Decimal("1234.5678"),
        "Day": dt.date(2024, 2, 29),
        "At": dt.time(13, 14, 15, 123456),
        "Local": dt.datetime(2024, 1, 2, 3, 4, 5, 678901),
        # 03:04:05.678901 at +02:00 is 01:04:05.678901 UTC.
        "Instant": dt.datetime(2024, 1, 2, 1, 4, 5, 678901, tzinfo=UTC),
        "Elapsed": dt.timedelta(days=1, hours=2, minutes=3, seconds=4, microseconds=567891),
        # RFC 4122 byte order: uuid.bytes is exactly what the generator stores.
        "Key": uuid.UUID("00112233-4455-6677-8899-aabbccddeeff").bytes,
        "Level": 2,
        "Big": 2**64 - 1,
    },
    {
        "Id": -2,
        "Count": None,
        "Score": -2.25,
        "Flag": False,
        "Name": "unicode é中",
        "Note": None,
        "Payload": b"",
        "Amount": Decimal("-0.0001"),
        "Day": dt.date(1969, 12, 31),
        "At": dt.time(0, 0),
        "Local": dt.datetime(1900, 1, 1),
        "Instant": dt.datetime(1970, 1, 1, tzinfo=UTC),
        "Elapsed": dt.timedelta(milliseconds=-1500),
        "Key": uuid.UUID(int=0).bytes,
        "Level": 0,
        "Big": 0,
    },
    {
        "Id": 2**63 - 1,
        "Count": -(2**31),
        "Score": 1e300,
        "Flag": True,
        "Name": "",
        "Note": "",
        "Payload": bytes([42]),
        "Amount": Decimal("99999999999999.9999"),
        "Day": dt.date(9999, 12, 31),
        "At": dt.time(23, 59, 59, 999999),
        "Local": dt.datetime(9999, 12, 31, 23, 59, 59, 999999),
        "Instant": dt.datetime(1, 1, 1, tzinfo=UTC),
        "Elapsed": dt.timedelta(0),
        "Key": uuid.UUID("ffffffff-ffff-ffff-ffff-ffffffffffff").bytes,
        "Level": 2,
        "Big": 2**63,
    },
]


def verify(path: str) -> list[str]:
    with pa.memory_map(path) as source:
        table = ipc.open_file(source).read_all()

    problems: list[str] = []
    table.validate(full=True)

    for (name, expected_type, nullable), field in zip(SCHEMA, table.schema, strict=True):
        if field.name != name:
            problems.append(f"field order: expected {name}, found {field.name}")
        if field.type != expected_type:
            problems.append(f"{name}: expected {expected_type}, found {field.type}")
        if field.nullable != nullable:
            problems.append(f"{name}: expected nullable={nullable}, found {field.nullable}")

    rows = table.to_pylist()
    if len(rows) != len(ROWS):
        problems.append(f"expected {len(ROWS)} rows, found {len(rows)}")
    for index, (expected, actual) in enumerate(zip(ROWS, rows)):
        for name, _, _ in SCHEMA:
            if expected[name] != actual[name]:
                problems.append(f"row {index} {name}: expected {expected[name]!r}, found {actual[name]!r}")
    return problems


def produce(path: str) -> None:
    # PyArrow's defaults, not the generator's: every field nullable. The generated reader checks
    # nulls on the data, so this must still be accepted.
    schema = pa.schema([pa.field(name, type_) for name, type_, _ in SCHEMA])
    table = pa.Table.from_pylist(ROWS, schema=schema)
    with ipc.new_file(path, schema) as writer:
        writer.write_table(table, max_chunksize=2)  # two batches: exercises FromRecordBatches


def main() -> int:
    if len(sys.argv) != 3 or sys.argv[1] not in ("verify", "produce"):
        print(__doc__, file=sys.stderr)
        return 2

    command, path = sys.argv[1], sys.argv[2]
    if command == "produce":
        produce(path)
        print(f"produced {len(ROWS)} rows at {path} with pyarrow {pa.__version__}")
        return 0

    problems = verify(path)
    for problem in problems:
        print(problem, file=sys.stderr)
    if problems:
        return 1
    print(f"pyarrow {pa.__version__} verified {len(ROWS)} generated rows: schema, types and values match")
    return 0


if __name__ == "__main__":
    sys.exit(main())
