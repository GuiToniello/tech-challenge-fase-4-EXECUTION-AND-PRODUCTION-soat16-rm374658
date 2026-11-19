namespace TechChallenge.Oficina.Execution.Messaging.Sqs;

public sealed record MensagemSqs(string MessageId, string ReceiptHandle, string Body);
