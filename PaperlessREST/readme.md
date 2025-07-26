
In practice
If you want all of the above, you’d:

In Program.cs

    Add API versioning & Versioned Explorer

    In your AddOpenApi(...) call, register security + dynamic servers + any transformers.

    Tag your endpoints (.WithTags(...)) or use transformers to group them.

    In your .csproj

    Keep OpenApiGenerateDocuments props.

    Optionally wrap the <Target Name="Kiota_GenerateClient"> in a Condition so it only runs in CI (e.g. Condition="'$(CI)'=='true'").

    CI pipeline

    Install Kiota with microsoft/setup-kiota@v0.x.

    Run a standalone kiota generate step (so dev machines aren’t slowed by codegen).
| Kiota advanced: auth provider injection | Let your generated client automatically use MSAL, default headers, logging, retry policies. |

xml
Copy
Edit
<!-- MSBuild target -->
--language CSharp \
--namespace $(AssemblyName).Client \
--openapi … \
--credential-scopes https://graph.microsoft.com/.default \
--auth-method azureManagedIdentity
|
| CI‑only client gen | Keep local builds fast, shift codegen into GitHub Actions. | Use the setup‑kiota action and drop the MSBuild Exec target.
