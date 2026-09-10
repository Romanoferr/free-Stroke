// Dedup mouse↔stylus: o WPF dispara eventos Stylus* E Mouse* para o mesmo
// clique físico (promoção). Regra segura: mouse é ignorado se houve evento
// stylus nos últimos 80 ms — promoção é síncrona (mesmo batch), então duplo
// clique legítimo nunca cai na janela (o anterior relevante seria mouse, não stylus).
// Função pura de doubles para ser unit-testável; o relógio mora na Shell.

namespace EpicPencil.Core;

public static class InputDedup
{
    public const double MouseSuppressWindowMs = 80;

    public static bool ShouldAcceptMouse(double lastStylusMs, double nowMs) =>
        double.IsNaN(lastStylusMs) || (nowMs - lastStylusMs) > MouseSuppressWindowMs;

    public static double NowMs() =>
        (double)System.Diagnostics.Stopwatch.GetTimestamp() * 1000
        / System.Diagnostics.Stopwatch.Frequency;
}
