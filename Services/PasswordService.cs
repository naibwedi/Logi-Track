using Microsoft.AspNetCore.Identity;
using System;
namespace logirack.Services;
public class PasswordService
{
    public string GeneratePassword()
    {
        // Create a PasswordOptions object with required options
        var passwordOptions = new PasswordOptions
        {
            RequireDigit = true,
            RequiredLength = 12,
            RequireLowercase = true,
            RequireNonAlphanumeric = true,
            RequireUppercase = true,
        };

        // Generate a random password
        string password = GeneratePasswordWithOptions(passwordOptions);
        return password;
    }

    private string GeneratePasswordWithOptions(PasswordOptions pOptions)
    {
        var random = new Random();
        const string upperC = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string loweC = "abcdefghijklmnopqrstuvwxyz";
        const string numbers = "0123456789";
        const string nonAlphanumeric = "!@#$%^&*()_-+=[{]}|;:',.<>?\"";
        
        //ensure we have at least one of each  required character type 
        var password = new List<char>();
        if (pOptions.RequireDigit)
            password.Add(numbers[random.Next(numbers.Length)]);
        if (pOptions.RequireLowercase)
            password.Add(loweC[random.Next(loweC.Length)]);
        if(pOptions.RequireNonAlphanumeric)
            password.Add(nonAlphanumeric[random.Next(nonAlphanumeric.Length)]);
        if (pOptions.RequireUppercase)
            password.Add(upperC[random.Next(upperC.Length)]);
        //the rest with random char
        string allchars=upperC+numbers+nonAlphanumeric+loweC;
        while (password.Count<pOptions.RequiredLength)
            password.Add(allchars[random.Next(allchars.Length)]);
        
        return new string(password.OrderBy(x => x).ToArray());
    }
}