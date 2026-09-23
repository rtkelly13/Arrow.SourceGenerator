# Arrow.SourceGenerator

Roslyn incremental source generator that maps `[ArrowSerializable]` .NET models to Apache Arrow at
compile time: a generated `Schema`, model collection → `RecordBatch`, and strict `RecordBatch` →
model materialisation. No reflection; Native AOT and trimming friendly.

Documentation: https://github.com/rtkelly13/Arrow.SourceGenerator
