using Nieweb.Data.Entities;

namespace Nieweb.Api.ProductionLines;

/// <summary>
/// Read/write access to <see cref="ProductionLine"/> and its associated
/// <see cref="ProductionLineMachine"/> assignments. Backs the admin
/// "Production lines" page (Vieweb §2.4.3 / docs/phase-2.md §7.4
/// <c>PL1</c>) and is the anchor for the Process Capability dashboard
/// (<c>PC1</c>).
/// </summary>
/// <remarks>
/// A machine identity is <c>(sourceId, machineId)</c>. The service
/// enforces the "at most one line per physical machine" rule at the DB
/// layer via a unique index; attempts to add a second assignment for
/// the same physical machine throw
/// <see cref="ProductionLineConflictException"/>.
/// </remarks>
public interface IProductionLines
{
    /// <summary>Returns every line, ordered by (DisplayOrder, Name).</summary>
    Task<IReadOnlyList<ProductionLineRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the line with its assignments, or <c>null</c> if none.
    /// Machines are ordered by (DisplayOrder, MachineName).
    /// </summary>
    Task<ProductionLineDetail?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new line. Throws
    /// <see cref="ProductionLineConflictException"/> if the name is
    /// already used.
    /// </summary>
    Task<ProductionLineRow> CreateAsync(
        string name,
        int displayOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames / re-sorts a line. Returns <c>null</c> when the line does
    /// not exist. Throws <see cref="ProductionLineConflictException"/> on
    /// name clash.
    /// </summary>
    Task<ProductionLineRow?> UpdateAsync(
        int id,
        string name,
        int displayOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the line (cascading its machine assignments). Returns
    /// <c>true</c> if a row was removed.
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a machine to the line. Returns <c>null</c> if the line
    /// does not exist. Throws <see cref="ProductionLineConflictException"/>
    /// when the same physical machine is already assigned to any line.
    /// </summary>
    Task<ProductionLineMachineRow?> AddMachineAsync(
        int lineId,
        string sourceId,
        int machineId,
        string machineName,
        string? category,
        int displayOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a machine assignment. Returns <c>true</c> if a row was
    /// removed. The line itself is left intact.
    /// </summary>
    Task<bool> RemoveMachineAsync(
        int lineId,
        int machineAssignmentId,
        CancellationToken cancellationToken = default);
}

/// <summary>Row snapshot returned by list / create / update.</summary>
public sealed record ProductionLineRow(
    int Id,
    string Name,
    int DisplayOrder,
    int MachineCount,
    DateTime CreatedUtc,
    DateTime LastModifiedUtc);

/// <summary>Row snapshot for a single machine assignment.</summary>
public sealed record ProductionLineMachineRow(
    int Id,
    int ProductionLineId,
    string SourceId,
    int MachineId,
    string MachineName,
    string? Category,
    int DisplayOrder,
    DateTime CreatedUtc);

/// <summary>Line detail (line + all its machines).</summary>
public sealed record ProductionLineDetail(
    ProductionLineRow Line,
    IReadOnlyList<ProductionLineMachineRow> Machines);

/// <summary>
/// Thrown when a create / update / add-machine call would violate a
/// uniqueness invariant (duplicate line name, duplicate machine
/// assignment). Endpoints surface these as HTTP 409.
/// </summary>
public sealed class ProductionLineConflictException : InvalidOperationException
{
    public ProductionLineConflictException(string message) : base(message) { }
    public ProductionLineConflictException(string message, Exception inner) : base(message, inner) { }
    public ProductionLineConflictException() { }
}
