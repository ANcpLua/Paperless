// Test infrastructure is not product code, so it is not measured.
//
// Coverage answers "did the tests exercise this line". Asking that of the fixtures
// the tests are built from is circular: every helper the suites touch reports as
// covered by construction, and the only thing a number here can reveal is a dead
// helper — which an unused-symbol warning already tells us, more directly.
//
// Left measured it also distorts the gate. These 468 lines sat in the same
// aggregate as PaperlessREST and PaperlessServices, so ContainerFixtureBase
// (77.2%) and FakeLoggerExtensions (83.3%) depressed the number that decides
// whether a push to main passes, while saying nothing about product risk.
//
// This attribute excludes the assembly at collection time, so the raw Cobertura
// never contains it and every downstream consumer — the DotCov gate, Codecov, the
// ReportGenerator HTML — agrees without needing its own rule. The filters in
// Pipeline/Components/ICoverage.cs and codecov.yml still name TestSupport, but as
// a backstop should this attribute ever be dropped, not as the mechanism.

[assembly: ExcludeFromCodeCoverage]
