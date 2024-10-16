namespace logirack.Data;
using logirack.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

public class SeedData
{
    public static async Task Initialize(IServiceProvider serviceProvider)
    {
        using var db = new ApplicationDbContext(
            serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()
            );
        
    }
    
}