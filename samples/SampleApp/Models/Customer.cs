namespace SampleApp.Models;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Category { get; set; } = "";  // 個人/法人
    public bool IsActive { get; set; }
}
