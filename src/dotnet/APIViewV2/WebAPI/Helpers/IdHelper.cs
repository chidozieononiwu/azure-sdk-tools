namespace WebAPI.Helpers
{
    public class IdHelper
    {
        public static string GenerateId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
