using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using tech_project_back_end.Data;
using tech_project_back_end.Models;
using tech_project_back_end.Repository.IRepository;

namespace tech_project_back_end.Repository;

public class CartRepository : ICartRepository
{
    private readonly AppDbContext _db;
    internal DbSet<Cart> _dbSet;

    public CartRepository(AppDbContext db)
    {
        _db = db;
        this._dbSet = _db.Set<Cart>();
    }

    public async Task<IQueryable<Cart>> GetCart(Expression<Func<Cart, bool>>? filter = null, bool tracked = true)
    {
        IQueryable<Cart> query = _dbSet;

        if (tracked)
        {
            query = query.AsNoTracking();
        }

        if (filter != null)
        {
            query = query.Where(filter);
        }

        return query;
    }
    
    public async Task<List<CartProductModel>> GetCartProduct(string userId)
    {
        var productsInCart = await _dbSet.Where(cart => cart.user_id == userId)
            .Join(_db.Product, cart => cart.product_id, prod => prod.product_id, (cart, prod) => new { cart, prod })
            .Select(combined => new CartProductModel
            {
                Product = new Product
                {
                    product_id = combined.prod.product_id,
                    name_pr = combined.prod.name_pr,
                    name_serial = combined.prod.name_serial,
                    detail = combined.prod.detail,
                    price = combined.prod.price,
                    quantity_pr = combined.prod.quantity_pr,
                    guarantee_period = combined.prod.guarantee_period,
                    supplier_id = combined.prod.supplier_id
                },
                Quantity = combined.cart.quantity,
                Category = _db.ProductCategory.
                    Where(pc => pc.product_id == combined.prod.product_id).
                    Join(_db.Category, pc => pc.category_id, c => c.category_id, (pc, c) => new Category
                    {
                        category_id = c.category_id,
                        category_name = c.category_name
                    }).SingleOrDefault(),
                Supplier = _db.Supplier.Where(sup => sup.SupplierId == combined.prod.supplier_id).
                    Select(sup => new SupplierModel
                    {
                        SupplierId = sup.SupplierId,
                        SupplierName = sup.SupplierName
                    }).SingleOrDefault(),
                Image = _db.Image.Where(i => i.ProductId == combined.prod.product_id).FirstOrDefault()
            }).ToListAsync();
        return productsInCart;
    }

    public async Task<Cart> Create(Cart entity)
    {
        //Add new Cart
        var newCart = await _dbSet.AddAsync(entity);
        
        var productToUpdate = await _db.Product.FindAsync(entity.product_id);
        int newQuantity = productToUpdate.quantity_pr - entity.quantity;
        await UpdateQuantityOfProduct(newQuantity, productToUpdate);
        
        await Save();
        return newCart.Entity;
    }

    public async Task<Cart> Update(Cart entity)
    {
        if (entity.quantity == 0)
        {
            _dbSet.Remove(entity);
            await Save();
            return null;
        }
        else
        {
            var cartToUpdate = await GetCart(c => c.product_id == entity.product_id && c.user_id == entity.user_id).Result.FirstOrDefaultAsync(); 

            var productToUpdate = await _db.Product.FindAsync(entity.product_id);
        
            int newQuantity = productToUpdate.quantity_pr + cartToUpdate.quantity - entity.quantity;
            if (newQuantity < 0)
            {
                throw new Exception("Quantity cannot be negative");
            }

            cartToUpdate.quantity = entity.quantity;
            _db.Entry(cartToUpdate).State = EntityState.Modified;

            await UpdateQuantityOfProduct(newQuantity, productToUpdate);

            await Save();
        
            return cartToUpdate;
        }
    }

    public async Task Delete(Cart entity)
    {
        var productToUpdate = await _db.Product.FindAsync(entity.product_id);
        await UpdateQuantityOfProduct(productToUpdate.quantity_pr + entity.quantity, productToUpdate);
        _dbSet.RemoveRange(entity);
        await Save();
    }

    public async Task Save()
    {
        await _db.SaveChangesAsync();
    }

    private async Task UpdateQuantityOfProduct(int newQuantity, Product productToUpdate)
    {
        productToUpdate.quantity_pr = newQuantity;
        _db.Entry(productToUpdate).State = EntityState.Modified;
        await Save();
    }
}