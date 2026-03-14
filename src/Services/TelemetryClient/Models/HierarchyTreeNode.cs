namespace TelemetryClient.Models;

/// <summary>
/// Represents a node in the building hierarchy tree
/// </summary>
public class HierarchyTreeNode
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Status { get; set; }
    public bool IsExpanded { get; set; }
    public bool ChildrenLoaded { get; set; }
    public List<HierarchyTreeNode> Children { get; set; } = new();
    public Dictionary<string, object>? Attributes { get; set; }

    /// <summary>
    /// Get icon for node type
    /// </summary>
    public string GetIcon() => NodeType switch
    {
        "Site" => "@Icons.Material.Filled.LocationCity",
        "Building" => "@Icons.Material.Filled.Business",
        "Level" => "@Icons.Material.Filled.Layers",
        "Area" => "@Icons.Material.Filled.MeetingRoom",
        "Equipment" => "@Icons.Material.Filled.Precision Manufacturing",
        "Device" => "@Icons.Material.Filled.Sensors",
        _ => "@Icons.Material.Filled.Circle"
    };

    /// <summary>
    /// Determine if this node type can have children
    /// </summary>
    public bool CanHaveChildren() => NodeType switch
    {
        "Device" => false, // Stop at Device level
        _ => true
    };
}
