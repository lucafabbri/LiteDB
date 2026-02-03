using Community.LiteDB.Aot.Context;
using Community.LiteDB.Aot.Collections;
using Community.LiteDB.Aot.ModelBuilder;
using Community.LiteDB.Aot.Sample.Domain;

namespace Community.LiteDB.Aot.Sample.Infrastructure;

/// <summary>
/// Sample DbContext - EF Core style configuration
/// Source generator will create mappers based on OnModelCreating
/// </summary>
public partial class AppDbContext : LiteDbContext
{
    // Collection properties
    public AotLiteCollection<Customer> Customers => Collection<Customer>();
    // public AotLiteCollection<Product> Products => Collection<Product>();  // Temporarily disabled - Range validation issue
    public AotLiteCollection<CustomerWithAddress> CustomersWithAddress => Collection<CustomerWithAddress>();
    public AotLiteCollection<Order> Orders => Collection<Order>();
    public AotLiteCollection<Company> Companies => Collection<Company>();
    public AotLiteCollection<ProductWithMoney> ProductsWithMoney => Collection<ProductWithMoney>();
    public AotLiteCollection<UserProfile> UserProfiles => Collection<UserProfile>();
    
    public AppDbContext(string filename) : base(filename)
    {
    }
    
    protected override void OnModelCreating(EntityModelBuilder modelBuilder)
    {
        // Configure Customer entity
        modelBuilder.Entity<Customer>(entity =>
        {
            // Primary key with auto-increment
            entity.HasKey(x => x.Id).AutoIncrement();
            
            // Properties
            entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
            entity.Property(x => x.Email).IsRequired().HasIndex("idx_email").IsUnique();
            entity.Property(x => x.City).HasIndex("idx_city");
            
            // Custom collection name
            entity.ToCollection("customers");
        });
        
        /* TEMPORARILY DISABLED - Range validation issues
        // Configure Product entity (uses Data Annotations - no config needed!)
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToCollection("products");
        });
        */
        
        // Configure CustomerWithAddress entity (with nested Address object)
        modelBuilder.Entity<CustomerWithAddress>(entity =>
        {
            entity.HasKey(x => x.Id).AutoIncrement();
            entity.ToCollection("customers_with_address");
        });
        
        // Configure Order entity (with 3 levels of nesting!)
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(x => x.Id).AutoIncrement();
            entity.ToCollection("orders");
        });
        
        // Configure Company entity (with List<Address> - collection of nested objects!)
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(x => x.Id).AutoIncrement();
            entity.ToCollection("companies");
        });
        
        // Configure ProductWithMoney entity (DDD with private setters!)
        modelBuilder.Entity<ProductWithMoney>(entity =>
        {
            entity.HasKey(x => x.Id).AutoIncrement();
            entity.ToCollection("products_with_money");
        });
        
        // Configure UserProfile entity (Full Data Annotations support demo!)
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(x => x.Id).AutoIncrement();
            entity.ToCollection("user_profiles");
        });
    }
}

