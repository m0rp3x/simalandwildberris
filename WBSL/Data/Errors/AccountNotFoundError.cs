namespace WBSL.Data.Errors;

public class AccountNotFoundError : Exception
{
    public AccountNotFoundError(string message) : base(message){
        
    }
    
}