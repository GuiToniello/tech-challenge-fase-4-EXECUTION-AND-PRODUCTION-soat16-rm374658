namespace TechChallenge.Oficina.Execution.Controllers.Features.Execucoes
{
    public sealed class ExecucoesResult<TValue, TError>
    {
        public TValue? Value { get; set; }
        public TError? Error { get; set; }

        public ExecucoesResult() { }

        public ExecucoesResult(TValue result)
        {
            Value = result;
            Error = default;
        }

        public ExecucoesResult(TError error)
        {
            Value = default;
            Error = error;
        }
    }

    public static class ExecucoesResult
    {
        public static ExecucoesResult<TValue, Exception> From<TValue>(TValue result)
            => new(result);

        public static ExecucoesResult<TValue, Exception> FromError<TValue>(Exception error)
            => new(error);
    }
}
