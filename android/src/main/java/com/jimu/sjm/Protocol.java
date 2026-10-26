package com.jimu.sjm;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** One challenge and one authenticated command per TCP connection. No key travels over the network. */
public final class Protocol {
    public static final int PORT = 39871;
    public static String randomHex(int bytes) {
        byte[] b = new byte[bytes]; new SecureRandom().nextBytes(b); return hex(b);
    }
    public static String hex(byte[] bytes) {
        StringBuilder out = new StringBuilder();
        for (byte b : bytes) out.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
        return out.toString();
    }
    public static String sign(String key, String challenge, String command) throws GeneralSecurityException {
        Mac mac = Mac.getInstance("HmacSHA256");
        mac.init(new SecretKeySpec(key.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
        return hex(mac.doFinal((challenge + "\n" + command).getBytes(StandardCharsets.UTF_8)));
    }
    public static boolean verify(String key, String challenge, String command, String signature) throws GeneralSecurityException {
        if (!command.matches("UP|DOWN|STATUS|MOUSE|MOUSE2|FOCUS|TEXT:[A-Za-z0-9+/=]{0,80}") || !signature.matches("[0-9a-f]{64}")) return false;
        return MessageDigest.isEqual(sign(key, challenge, command).getBytes(StandardCharsets.US_ASCII), signature.getBytes(StandardCharsets.US_ASCII));
    }
    public static String readLine(InputStream input) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        for (int i = 0; i < 160; i++) {
            int b = input.read();
            if (b == -1) throw new EOFException();
            if (b == '\n') return out.toString(StandardCharsets.US_ASCII.name());
            if (b < 32 || b > 126) throw new IOException("Invalid character");
            out.write(b);
        }
        throw new IOException("Line too long");
    }
}
