using SampleApp.Models;

namespace SampleApp.Data;

public class CustomerRepository
{
    private readonly List<Customer> _customers;
    private int _nextId;

    public CustomerRepository()
    {
        _customers = new List<Customer>
        {
            new() { Id = 1, Name = "田中太郎", Phone = "090-1234-5678", Email = "tanaka@example.com", Category = "個人", IsActive = true },
            new() { Id = 2, Name = "鈴木花子", Phone = "03-1234-5678", Email = "suzuki@example.com", Category = "法人", IsActive = true },
            new() { Id = 3, Name = "佐藤一郎", Phone = "080-9876-5432", Email = "sato@example.com", Category = "個人", IsActive = true },
            new() { Id = 4, Name = "高橋美咲", Phone = "06-5555-1234", Email = "takahashi@example.com", Category = "法人", IsActive = false },
            new() { Id = 5, Name = "山田健一", Phone = "070-1111-2222", Email = "yamada@example.com", Category = "個人", IsActive = true },
        };
        _nextId = 6;
    }

    public IReadOnlyList<Customer> GetAll() => _customers.AsReadOnly();

    public IReadOnlyList<Customer> Search(string field, string condition, bool partialMatch)
    {
        if (string.IsNullOrEmpty(condition))
            return _customers.AsReadOnly();

        Func<Customer, string> selector = field switch
        {
            "名前" => c => c.Name,
            "電話番号" => c => c.Phone,
            "メール" => c => c.Email,
            _ => c => c.Name,
        };

        return _customers
            .Where(c => partialMatch
                ? selector(c).Contains(condition, StringComparison.OrdinalIgnoreCase)
                : selector(c).Equals(condition, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    public void Add(Customer customer)
    {
        customer.Id = _nextId++;
        _customers.Add(customer);
    }

    public Customer? GetById(int id) => _customers.FirstOrDefault(c => c.Id == id);
}
