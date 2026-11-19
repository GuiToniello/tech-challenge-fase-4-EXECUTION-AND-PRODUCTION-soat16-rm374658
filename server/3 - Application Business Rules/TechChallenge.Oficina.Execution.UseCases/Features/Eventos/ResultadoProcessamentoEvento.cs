namespace TechChallenge.Oficina.Execution.UseCases.Features.Eventos;

// Processado: o evento alterou o item da producao. Ignorado: evento repetido ou tipo nao tratado.
// Nos dois casos a mensagem pode sair da fila; so uma excecao faz o evento ser reprocessado.
public enum ResultadoProcessamentoEvento
{
    Processado,
    Ignorado
}
