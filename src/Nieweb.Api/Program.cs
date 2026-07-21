// Minimal Nieweb.Api host. Endpoints, DI wiring, and middleware get added
// as their respective Phase 1 backlog items land (A1/A2 for source
// registration, I1..I4 for auth, R3/R4/R5 for the report endpoints, etc.).

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
