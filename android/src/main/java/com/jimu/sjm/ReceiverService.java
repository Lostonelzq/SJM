package com.jimu.sjm;

import android.app.*;
import android.content.*;
import android.os.*;
import java.net.*;
import java.io.*;
import java.nio.charset.StandardCharsets;

public class ReceiverService extends Service {
    public static volatile boolean running;
    public static volatile String status = "尚未开始接收";
    private static volatile long lastContact;
    public static String displayStatus() {
        if (running && lastContact > 0 && SystemClock.elapsedRealtime()-lastContact > 15000)
            return "接收已开启，电脑暂未通信 · 请检查两端网络和 IP";
        return status;
    }
    private volatile boolean alive;
    private volatile ServerSocket server;
    private final java.util.Set<Socket> clients=java.util.concurrent.ConcurrentHashMap.newKeySet();
    private final java.util.concurrent.ExecutorService workers=java.util.concurrent.Executors.newFixedThreadPool(4);
    private final java.util.concurrent.atomic.AtomicBoolean mouseActive=new java.util.concurrent.atomic.AtomicBoolean();
    public static String key(Context context) {
        var prefs = context.getSharedPreferences("pairing", MODE_PRIVATE);
        String key = prefs.getString("key", null);
        if (key == null) { key = Protocol.randomHex(12); prefs.edit().putString("key", key).apply(); }
        return key;
    }
    @Override public IBinder onBind(Intent i) { return null; }
    @Override public int onStartCommand(Intent intent, int flags, int id) {
        if (intent != null && "STOP".equals(intent.getAction())) { stopSelf(); return START_NOT_STICKY; }
        if (alive) return START_NOT_STICKY;
        NotificationManager nm = getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel("control", "手机控制", NotificationManager.IMPORTANCE_LOW));
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop = PendingIntent.getService(this, 1, new Intent(this, ReceiverService.class).setAction("STOP"), PendingIntent.FLAG_IMMUTABLE);
        startForeground(1, new Notification.Builder(this, "control").setContentTitle("轻划 · 正在接收电脑指令")
            .setContentText("滑动、鼠标点击与输入测试 · 可随时停止")
            .setSmallIcon(com.jimu.sjm.R.drawable.ic_swipe).setContentIntent(open).setOngoing(true)
            .addAction(new Notification.Action.Builder(null, "停止接收", stop).build()).build());
        alive = true;
        new Thread(this::listen, "sjm-receiver").start();
        return START_NOT_STICKY;
    }
    private void listen() {
        try (ServerSocket socket = TlsIdentity.create()) {
            server = socket;
            socket.setReuseAddress(true); socket.bind(new InetSocketAddress(Protocol.PORT));
            lastContact = 0; running = true; status = "接收已开启，等待电脑连接";
            while (alive) {
                Socket connection = socket.accept();
                if(clients.size()>=4) { connection.close(); continue; }
                clients.add(connection); workers.execute(()->handle(connection));
            }
        } catch (Exception e) { status="无法开启接收，请停止后重试"; }
        finally { running=false; alive=false; stopSelf(); }
    }
    private void handle(Socket connection) {
                try (Socket ignoredConnection = connection) {
                    
                    if (!alive) return;
                    InetAddress peer = connection.getInetAddress();
                    if (!peer.isSiteLocalAddress() && !peer.isLoopbackAddress() && !peer.isLinkLocalAddress()) return;
                    connection.setSoTimeout(2000);
                    connection.setTcpNoDelay(true);
                    ((javax.net.ssl.SSLSocket)connection).startHandshake();
                    PrintWriter out = new PrintWriter(new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.US_ASCII), true);
                    String challenge = Protocol.randomHex(16);
                    out.println("SJM1 " + challenge);
                    String[] request = Protocol.readLine(connection.getInputStream()).split(" ", -1);
                    if (request.length == 2 && request[0].equals("PAIR")) {
                        if (alive && Pairing.redeem(request[1])) {
                            out.println("PAIRED " + key(this));
                            getSharedPreferences("pairing",MODE_PRIVATE).edit().putBoolean("paired",true).apply();
                            status="电脑配对成功 · 可以开始控制";
                        } else out.println("ERR PAIRING");
                        return;
                    }
                    if (request.length != 2 || !Protocol.verify(key(this), challenge, request[0], request[1])) { out.println("ERR AUTH"); return; }
                    if (!alive) { out.println("ERR STOPPED"); return; }
                    lastContact = SystemClock.elapsedRealtime();
                    SwipeService swipe = SwipeService.instance;
                    if(request[0].equals("MOUSE") || request[0].equals("MOUSE2")) { mouseLoop(connection,out,swipe,request[0].equals("MOUSE2")); return; }
                    String response = swipe == null ? "ERR ACCESSIBILITY" : request[0].equals("STATUS") ? "OK" : (request[0].equals("UP") || request[0].equals("DOWN")) ? swipe.swipe(request[0].equals("UP")) : swipe.control(request[0]);
                    out.println(response);
                    status = response.startsWith("OK") ? "电脑已连接 · 指令已完成" : "执行失败：" + response;
                } catch (Exception e) { if (!alive) return; android.util.Log.w("SJM", "Connection failed: " + e.getClass().getSimpleName() + ": " + e.getMessage()); }
                finally { clients.remove(connection); }
    }
    private void mouseLoop(Socket connection,PrintWriter out,SwipeService swipe,boolean streaming) throws IOException {
        if(swipe==null) { out.println("ERR ACCESSIBILITY"); return; }
        if(!mouseActive.compareAndSet(false,true)) { out.println("ERR BUSY"); return; }
        try {
            out.println("OK");
            while(alive) {
                String command=Protocol.readLine(connection.getInputStream());
                if(!command.matches("(MOVE|TAP):[0-9]{1,5}:[0-9]{1,5}|HIDE|PING")) { out.println("ERR COMMAND"); return; }
                lastContact=SystemClock.elapsedRealtime();
                if(streaming && command.startsWith("MOVE:")) {
                    String response=swipe.movePointer(command);
                    if(!response.equals("OK")) { out.println(response);return; }
                    continue;
                }
                if(command.equals("PING")) { swipe.keepPointer();out.println("OK");continue; }
                String response=swipe.control(command); out.println(response);
                if(out.checkError() || command.equals("HIDE") || !response.equals("OK")) return;
            }
        } finally { swipe.hidePointer(); mouseActive.set(false); }
    }
    @Override public void onDestroy() {
        alive = false; running = false;
        try { if (server != null) server.close(); } catch (IOException ignored) { }
        for(Socket connection:clients) { try { connection.close(); } catch(IOException ignored) { } }
        workers.shutdownNow();
        if(SwipeService.instance!=null) SwipeService.instance.hidePointer();
        super.onDestroy();
    }
}
