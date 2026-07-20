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
    long PanelId,
    int MachineId,
    int LaneNumber,
    string? PanelBarCode,
    long PanelNumericDate,
    int NbOfValidCards,
    int TestTime,
    int PanelStatus,
    long AnomalyBr,
    long AnomalyAr,
    bool HasBeenReviewed,
    int NbOfTestedObject,
    int NbOfErrorObject,
    int OperatorId,
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

public sealed record Product(int ProductId, string ProductName);

public sealed record Recipe(int RecipeId, string RecipeName, int ProductId);
