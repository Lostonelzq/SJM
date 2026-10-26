package com.jimu.sjm;

import android.security.keystore.*;
import javax.net.ssl.*;
import javax.security.auth.x500.X500Principal;
import java.net.Socket;
import java.security.*;
import java.security.cert.X509Certificate;
import java.math.BigInteger;
import java.util.Date;

public final class TlsIdentity {
    public static SSLServerSocket create() throws Exception {
        String alias="sjm-local-tls-v3";
        KeyStore store=KeyStore.getInstance("AndroidKeyStore"); store.load(null);
        if(!store.containsAlias(alias)) {
            KeyPairGenerator generator=KeyPairGenerator.getInstance("RSA","AndroidKeyStore");
            generator.initialize(new KeyGenParameterSpec.Builder(alias,KeyProperties.PURPOSE_SIGN | KeyProperties.PURPOSE_DECRYPT)
                // Conscrypt signs an already-hashed TLS digest through a raw RSA Keystore operation.
                .setKeySize(2048).setDigests(KeyProperties.DIGEST_NONE,KeyProperties.DIGEST_SHA256,KeyProperties.DIGEST_SHA384,KeyProperties.DIGEST_SHA512)
                .setSignaturePaddings(KeyProperties.SIGNATURE_PADDING_RSA_PKCS1)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE,KeyProperties.ENCRYPTION_PADDING_RSA_PKCS1)
                .setCertificateSubject(new X500Principal("CN=SJM"))
                .setCertificateSerialNumber(BigInteger.ONE)
                .setCertificateNotBefore(new Date(0)).setCertificateNotAfter(new Date(4102444800000L)).build());
            generator.generateKeyPair();
        }
        PrivateKey key=(PrivateKey)store.getKey(alias,null);
        X509Certificate cert=(X509Certificate)store.getCertificate(alias);
        X509ExtendedKeyManager manager=new X509ExtendedKeyManager() {
            public String[] getClientAliases(String type,Principal[] issuers) { return null; }
            public String chooseClientAlias(String[] types,Principal[] issuers,Socket s) { return null; }
            public String[] getServerAliases(String type,Principal[] issuers) { return type.equals("RSA")?new String[]{alias}:null; }
            public String chooseServerAlias(String type,Principal[] issuers,Socket s) { return type.equals("RSA")?alias:null; }
            public X509Certificate[] getCertificateChain(String a) { return new X509Certificate[]{cert}; }
            public PrivateKey getPrivateKey(String a) { return key; }
        };
        SSLContext context=SSLContext.getInstance("TLSv1.2"); context.init(new KeyManager[]{manager},null,new SecureRandom());
        SSLServerSocket socket=(SSLServerSocket)context.getServerSocketFactory().createServerSocket();
        socket.setEnabledProtocols(new String[]{"TLSv1.2"});
        return socket;
    }
}
