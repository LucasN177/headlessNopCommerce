namespace Nop.Plugin.Misc.HeadlessApi.Models.ReturnRequests;

/// <summary>
/// Represents a request to submit a return request (withdrawal) for items of an order
/// </summary>
public record SubmitReturnRequestRequest
{
    /// <summary>
    /// Gets or sets the quantities to return by order item identifier, e.g. { "12": 1, "13": 2 }; items with quantity 0 are ignored
    /// </summary>
    public Dictionary<int, int> Quantities { get; set; } = new();

    /// <summary>
    /// Gets or sets the return reason identifier
    /// </summary>
    public int ReturnRequestReasonId { get; set; }

    /// <summary>
    /// Gets or sets the requested action identifier
    /// </summary>
    public int ReturnRequestActionId { get; set; }

    /// <summary>
    /// Gets or sets the comments
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    /// Gets or sets the GUID of the file uploaded by POST return-requests/upload
    /// </summary>
    public Guid UploadedFileGuid { get; set; }

    /// <summary>
    /// Gets or sets the withdrawal token of the email link (guest return requests); empty for registered customers
    /// </summary>
    public string WithdrawalToken { get; set; }
}
