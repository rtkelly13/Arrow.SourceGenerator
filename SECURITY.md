# Security Policy

Generated code reads `RecordBatch` data that may originate outside the process (IPC streams, Flight,
files). Treat any way to make generated code crash the process, corrupt memory, or silently accept
data that violates the generated schema as a security issue.

Please report vulnerabilities privately through GitHub's
[security advisory](https://github.com/rtkelly13/Arrow.SourceGenerator/security/advisories/new)
form rather than a public issue.
