import com.jimu.sjm.Protocol;
import com.jimu.sjm.Pairing;
import java.io.*;
public class ProtocolTest {
    static void require(boolean value) { if(!value) throw new AssertionError(); }
    public static void main(String[] args) throws Exception {
        String key="0123456789abcdef01234567", nonce="abcdef0123456789abcdef0123456789";
        String sig=Protocol.sign(key,nonce,"UP");
        require(Protocol.verify(key,nonce,"UP",sig));
        require(!Protocol.verify(key,nonce,"DOWN",sig));
        require(!Protocol.verify(key,Protocol.randomHex(16),"UP",sig));
        require(!Protocol.verify("wrong",nonce,"UP",sig));
        require(!Protocol.verify(key,nonce,"SHELL",Protocol.sign(key,nonce,"SHELL")));
        require(!Protocol.verify(key,nonce,"UP","00"));
        require(Protocol.verify(key,nonce,"MOUSE",Protocol.sign(key,nonce,"MOUSE")));
        require(Protocol.verify(key,nonce,"FOCUS",Protocol.sign(key,nonce,"FOCUS")));
        require(Protocol.verify(key,nonce,"TEXT:YWJj",Protocol.sign(key,nonce,"TEXT:YWJj")));
        require(!Protocol.verify(key,nonce,"TEXT:YWJj",Protocol.sign(key,nonce,"TEXT:YWJk")));
        require(!Protocol.verify(key,nonce,"TAP:1:1",Protocol.sign(key,nonce,"TAP:1:1")));
        require(Protocol.readLine(new ByteArrayInputStream("UP abc\n".getBytes())).equals("UP abc"));
        try { Protocol.readLine(new ByteArrayInputStream(new byte[200])); throw new AssertionError(); } catch(IOException expected) { }
        try { Protocol.readLine(new ByteArrayInputStream("no-newline".getBytes())); throw new AssertionError(); } catch(IOException expected) { }
        try { Protocol.readLine(new ByteArrayInputStream("x".repeat(170).getBytes())); throw new AssertionError(); } catch(IOException expected) { }
        Pairing.open(); String code=Pairing.visibleCode(); require(code.matches("[0-9]{6}"));
        require(Pairing.secondsLeft()<=120 && Pairing.secondsLeft()>115);
        String wrong=code.equals("000000")?"000001":"000000";
        for(int i=0;i<5;i++) require(!Pairing.redeem(wrong));
        require(Pairing.visibleCode()==null); require(!Pairing.redeem(code));
        Pairing.open(); code=Pairing.visibleCode(); require(Pairing.redeem(code)); require(!Pairing.redeem(code));
        System.out.println("PASS: authentication, replay isolation, command allowlist, bounded parser");
        System.out.println("PASS: six-digit code, pairing window, five-attempt lockout, one-time redemption");
        System.out.println("VECTOR "+sig);
    }
}
