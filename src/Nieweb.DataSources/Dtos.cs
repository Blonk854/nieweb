namespace Nieweb.DataSources;

/// <summary>
/// One page of rows plus paging metadata.
/// </summary>
/// <typeparam name="TRow">Row DTO type.</typeparam>
/// <typeparam name="TCursor">Keyset cursor type for the next page (null when exhausted).</typeparam>
public sealed record Page<TRow, TCursor>(
    IReadOnlyList<TRow> Rows,
    TCursor? NextCursor,
    bool HasMore)
    where TCursor : struct;

public sealed record PanelRow(
    int PanelId,
    int MachineId,
    int LaneNumber,
    string PanelBarCode,
    int PanelNumericDate,
    int NbOfValidCards,
    double TestTime,
    int PanelStatus,
    int AnomalyBr,
    int AnomalyAr,
    bool HasBeenReviewed,
    int NbOfTestedObject,
    int NbOfErrorObject,
    int? OperatorId,
    int ProductId,
    int RecipeId);

public sealed record CardRow(
    long PanelId,
    int CardIdOnPanel,
    int CardStatus,
    long AnomalyBr,
    long AnomalyAr,
    int NbOfTestedObject,
    int NbOfErrorObject);

public sealed record TestedObjectRow(
    long PanelId,
    int CardIdOnPanel,
    int ObjectId,
    int ObjectTypeId,
    long ErrorTable,
    int Status);

public sealed record Machine(int MachineId, int MachineType, string MachineName, string MachineTypeName);

public sealed record Product(int ProductId, string? ProductName, string? Revision, string? Description);

/// <summary>
/// A recipe (inspection program). <see cref="FileName"/> is the human-readable
/// name used throughout the UI - the AOI Superviseur schema calls this column
/// <c>File_Name</c> even though it's really the recipe name.
/// </summary>
public sealed record Recipe(
    int RecipeId,
    string? FileName,
    int ProductId,
    string? Author,
    int InspectedSideNb,
    string? InspectedSideName,
    string? Customer,
    string? ProductionStep,
    string? VariantName);
