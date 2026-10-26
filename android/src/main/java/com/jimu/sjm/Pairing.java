package com.jimu.sjm;

import java.security.*;
import java.nio.charset.StandardCharsets;

/** Short code is used only inside TLS, for a bounded, explicitly opened pairing window. */
public final class Pairing {
    private static String code;
    private static long deadline;
    private static int failures;
    public static synchronized void open() {
        code = String.format(java.util.Locale.ROOT,"%06d",new SecureRandom().nextInt(1000000));
        deadline = System.nanoTime()/1000000+120000; failures=0;
    }
    public static synchronized String visibleCode() { return secondsLeft()>0 && failures<5 ? code : null; }
    public static synchronized long secondsLeft() { return Math.max(0,(deadline-System.nanoTime()/1000000)/1000); }
    public static synchronized boolean redeem(String attempt) {
        if (visibleCode()==null) return false;
        if (!attempt.matches("[0-9]{6}") || !MessageDigest.isEqual(code.getBytes(StandardCharsets.US_ASCII),attempt.getBytes(StandardCharsets.US_ASCII))) {
            failures++; return false;
        }
        code=null; deadline=0; return true;
    }
}
