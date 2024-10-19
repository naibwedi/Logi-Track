using System.ComponentModel.DataAnnotations;

namespace logirack.Models;

public class AgeValidator
{

    public static bool IsValidAge(DateTime dateOdbirth, int minAge)
    {
        var today = DateTime.Today;
        var age = today.Year - dateOdbirth.Year;
        if (dateOdbirth > today.AddYears(-age)) age--;
        return age >= minAge;
    }

    public static string GetAgeValidationError(DateTime dateOdbirth, int minAge)
    {
        if (dateOdbirth > DateTime.Today)
        {
            return "Try again Obay ";
        }

        if (!IsValidAge(dateOdbirth, minAge))
        {
            return "Try Later ";
        }

        return string.Empty;
    }
}