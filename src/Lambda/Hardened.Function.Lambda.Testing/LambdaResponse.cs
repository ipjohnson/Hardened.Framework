namespace Hardened.Function.Lambda.Testing
{
    public class LambdaResponse
    {
        public LambdaResponse(int status, byte[] payload)
        {
            Status = status;
            Payload = payload;
        }

        public int Status { get; }

        public byte[] Payload { get; }
    }
}
