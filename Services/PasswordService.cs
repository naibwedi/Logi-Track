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

    private string GeneratePasswordWithOptions(PasswordOptions options)
    {
        // Use randomization to generate a password based on PasswordOptions
        var random = new Random();
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        string nonAlphanumericChars = "!@#$%^&*()_-+=[{]}|;:',.<>?";

        char[] password = new char[options.RequiredLength];
        for (int i = 0; i < options.RequiredLength; i++)
        {
            if (options.RequireNonAlphanumeric && i == 0)
            {
                // Ensure the first character is a non-alphanumeric character
                password[i] = nonAlphanumericChars[random.Next(nonAlphanumericChars.Length)];
            }
            else
            {
                // Fill the rest with alphanumeric characters
                password[i] = chars[random.Next(chars.Length)];
            }
        }

        return new string(password);
    }
}