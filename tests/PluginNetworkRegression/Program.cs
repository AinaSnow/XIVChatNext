using XIVChatPlugin;

internal static class Program {
    private static int checks;
    private static void Check(bool value, string name) {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name); checks++;
    }
    public static int Main() {
        try {
            long now = 0;
            var window = (long)ConnectionGate.TrustWindow.TotalMilliseconds;

            var gate = new ConnectionGate(() => now);
            for (var i = 0; i < ConnectionGate.MaxHandshakesPerSource; i++) gate.TryBeginHandshake("10.0.0.2");
            Check(!gate.TryBeginHandshake("10.0.0.2"), "A source cannot exceed the concurrent handshake limit");
            Check(gate.TryBeginHandshake("10.0.0.3"), "Another source is not affected by a busy source");
            gate.EndHandshake("10.0.0.2");
            Check(gate.TryBeginHandshake("10.0.0.2"), "Finishing a handshake frees a slot");
            for (var i = 0; i < ConnectionGate.MaxHandshakesPerSource; i++) gate.EndHandshake("10.0.0.2");
            gate.EndHandshake("10.0.0.3");
            gate.EndHandshake("10.0.0.3");
            Check(gate.Usage.Handshakes == 0, "Extra handshake releases do not underflow or leak entries");

            gate = new ConnectionGate(() => now);
            Check(gate.TryBeginTrustRequest("10.0.0.2"), "A first trust request is allowed");
            Check(!gate.TryBeginTrustRequest("10.0.0.2"), "Only one trust prompt per source can be pending");
            Check(gate.TryBeginTrustRequest("10.0.0.3"), "Pending prompts from other sources are independent");
            gate.EndTrustRequest("10.0.0.2");
            now += 1000;
            Check(gate.TryBeginTrustRequest("10.0.0.2"), "A source may retry after its prompt closes");
            gate.EndTrustRequest("10.0.0.2");
            now += 1000;
            Check(gate.TryBeginTrustRequest("10.0.0.2"), "The third request within the window is allowed");
            gate.EndTrustRequest("10.0.0.2");
            Check(!gate.TryBeginTrustRequest("10.0.0.2"), "A fourth request within one minute is refused");
            now += window;
            Check(gate.TryBeginTrustRequest("10.0.0.2"), "Requests are allowed again after the window passes");
            gate.EndTrustRequest("10.0.0.2");
            gate.EndTrustRequest("10.0.0.3");

            gate = new ConnectionGate(() => now);
            for (var i = 0; i < 300; i++) { gate.TryBeginTrustRequest("192.168.0." + i); gate.EndTrustRequest("192.168.0." + i); }
            now += window;
            Check(gate.TryBeginTrustRequest("192.168.0.1"), "Many short-lived sources do not block a later request");
            Check(gate.Usage.PendingTrust == 1, "Pending trust count reflects open prompts only");

            Console.WriteLine($"All {checks} plugin network checks passed.");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine("FAIL " + ex.Message);
            return 1;
        }
    }
}
