﻿using logirack.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace logirack.Data;
//// TOREAD dont use decimal on models use double for SQLite compatibility start 
/// <summary>
/// The database context for the LogiRack application.
/// Inherits from IdentityDbContext to include Identity functionality.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    { }
    //users and roles
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Driver> Drivers { get; set; }
    public DbSet<SuperAdmin> SuperAdmins { get; set; }
    public DbSet<Admin> Admins { get; set; }
    
    //actions
    public DbSet<Trip> Trips { get; set; }
    public DbSet<DriverTrip> DriverTrips { get; set; }
    public DbSet<PaymentPeriod> PaymentPeriods { get; set; }
    public DbSet<Payment> Payments { get; set; }
    
    //for driver
    public DbSet<Address> Addresses { get; set; }
    public DbSet<Location> Locations { get; set; }
    
    //admin
    public DbSet<AdminActionLog> AdminActionLogs { get; set; }
    public DbSet<RecentActivity> RecentActivities { get; set; }


    /// <summary>
    /// Configures the model relationships and properties using Fluent API.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model.</param>
    protected override void OnModelCreating(ModelBuilder mbuilder)
    {
        base.OnModelCreating(mbuilder);
        //----------------------------------||
        //          Inheritance             ||
        //--------------------------------- ||
         
        mbuilder.Entity<ApplicationUser>().HasDiscriminator<string>("RoleType")
            .HasValue<ApplicationUser>("ApplicationUser")
            .HasValue<SuperAdmin>("SuperAdmin")
            .HasValue<Admin>("Admin")
            .HasValue<Customer>("Customer")
            .HasValue<Driver>("Driver");
        
        //----------------------------------||
        // config relationships for SQLite  ||
        //--------------------------------- ||
        
        //IMPORTANT: see the diagram on discord or jira LOGIRACK-26 https://tools.uia.no/jira/browse/LOGIRACK-26
        //TODO i need to handle deletion restriction in app logic not here bcs
        //todo bcs sqlite not fully support of func OnDelete() and i need to ask the teacher about it 
        mbuilder.Entity<RecentActivity>()
            .HasOne(ra => ra.Trip)
            .WithMany(t => t.RecentActivities) // Ensure Trip has a collection of RecentActivities
            .HasForeignKey(ra => ra.TripId);
        //Customer : 
        //Customer 1-M  Trip                     
        mbuilder.Entity<Customer>()
            .HasMany(cuctomer => cuctomer.Trips)
            .WithOne(trip => trip.Customer)
            .HasForeignKey(trip => trip.CustomerId);
        
        
        ///<summary>
        /// $Driver relationship $
        /// driver 1-M Driver trip                  d = driver dt = DriverTrip
        /// driver 1-M PaymentPeriod
        /// driver 1-1 location
        /// driver 1-1 address
        /// </summary>
        mbuilder.Entity<RecentActivity>()
            .HasOne(ra => ra.Trip)
            .WithMany(t => t.RecentActivities) // Ensure Trip has a collection of RecentActivities
            .HasForeignKey(ra => ra.TripId);
        
        mbuilder.Entity<Driver>()
            .HasMany(d=>d.DriverTrips)
            .WithOne(dt => dt.Driver)
            .HasForeignKey(dt => dt.DriverId);
        
        // mbuilder.Entity<Driver>()
        //     .HasMany(driver => driver.PaymentPeriods)
        //     .WithOne(paymentP => paymentP.Driver)
        //     .HasForeignKey(paymentP => paymentP.DriverID);
        
        mbuilder.Entity<Driver>()
            .HasOne(d => d.PermanentAddress)
            .WithOne(a => a.Driver)
            .HasForeignKey<Address>(a => a.DriverId);
        
        mbuilder.Entity<Driver>()
            .HasOne(d=>d.CurrentLocation)
            .WithOne(l => l.Driver)
            .HasForeignKey<Location>(l => l.DriverID);
        
        ///<summary>
        /// $Admin table relationship $
        /// Admin 1-M  trip             
        /// admin 1-M DriverTrip
        /// amind 1-M Payment
        /// amind 1-M AAL
        /// </summary>
        
        // mbuilder.Entity<Admin>()
        //     .HasMany(a=>a.ManagedTrips)
        //     .WithOne(t => t.Admin)
        //     .HasForeignKey(trip => trip.AdminId);
        mbuilder.Entity<Trip>()
            .HasOne(t => t.Admin)
            .WithMany(a => a.ManagedTrips)
            .HasForeignKey(t => t.AdminId)
            .IsRequired(false);
        mbuilder.Entity<Admin>()
            .HasMany(a => a.AssignedDriverT)
            .WithOne(dt => dt.Admin)
            .HasForeignKey(dt => dt.AssignedByAdminId);
        
        mbuilder.Entity<Admin>()
            .HasMany(a=>a.ProcessedPayments)
            .WithOne(p => p.ProcByAdmin)
            .HasForeignKey(p=>p.ProcByAdminID);
        mbuilder.Entity<Admin>()
            .HasMany(a=>a.ActionLogs)
            .WithOne(aal =>aal.Admin )
            .HasForeignKey(aal=>aal.AdminId);
        //TODO Trip to .hasFK<DriverTrip> DriverTrip to PaymentPeriod PaymentPeriod to Payment
        //@ilyassb finish at  1:04
        //@ilyass start at 5:51 10/16

        //trip
        mbuilder.Entity<Trip>()
            .HasOne(t => t.DriverTrip)
            .WithOne(dt => dt.Trip)
            .HasForeignKey<DriverTrip>(dt => dt.TripId);
        
        //Driver trip
        mbuilder.Entity<DriverTrip>()
            .HasOne(dt => dt.PaymentPeriod)
            .WithMany(pp=>pp.DriverTrips)
            .HasForeignKey(pp=>pp.PaymentPeriodId);
        
        //Payment Period
        mbuilder.Entity<PaymentPeriod>()
            .HasMany(pp => pp.Payments)
            .WithOne(p=>p.PaymentPeriod)
            .HasForeignKey(p=>p.PaymentPeriodId);
 

        //----------------------------------||
        // Payment to Driver                ||
        //----------------------------------||

        mbuilder.Entity<Payment>()
            .HasOne(p => p.Driver)
            .WithMany(d => d.Payments)
            .HasForeignKey(p => p.DriverId);

        //----------------------------------||
        // Payment to Admin (ProcByAdmin)    ||
        //----------------------------------||

        mbuilder.Entity<Payment>()
            .HasOne(p => p.ProcByAdmin)
            .WithMany(a => a.ProcessedPayments)
            .HasForeignKey(p => p.ProcByAdminID);
        
    }
}
