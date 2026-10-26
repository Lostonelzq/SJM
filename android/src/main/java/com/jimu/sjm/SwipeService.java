package com.jimu.sjm;

import android.accessibilityservice.*;
import android.app.KeyguardManager;
import android.graphics.Path;
import android.os.*;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;
import java.util.concurrent.*;

public class SwipeService extends AccessibilityService {
    public static volatile SwipeService instance;
    private long lastSwipe;
    private PointerControl pointer;
    @Override protected void onServiceConnected() { pointer=new PointerControl(this); instance = this; }
    public String control(String command) { return pointer==null?"ERR ACCESSIBILITY":pointer.execute(command); }
    public void hidePointer() { if(pointer!=null) pointer.stop(); }
    public String movePointer(String command) { return pointer==null?"ERR ACCESSIBILITY":pointer.move(command); }
    public void keepPointer() { if(pointer!=null) pointer.keepAlive(); }
    @Override public void onAccessibilityEvent(AccessibilityEvent event) { }
    @Override public void onInterrupt() { }
    @Override public void onDestroy() { hidePointer(); instance = null; super.onDestroy(); }
    public String swipe(boolean up) {
        CountDownLatch done = new CountDownLatch(1);
        String[] result = {"ERR TIMEOUT"};
        new Handler(getMainLooper()).post(() -> {
            if (!ReceiverService.running) { result[0] = "ERR STOPPED"; done.countDown(); return; }
            if (getSystemService(KeyguardManager.class).isKeyguardLocked() || !getSystemService(PowerManager.class).isInteractive()) {
                result[0] = "ERR LOCKED"; done.countDown(); return;
            }
            long now = SystemClock.elapsedRealtime();
            if (now - lastSwipe < 450) { result[0] = "ERR BUSY"; done.countDown(); return; }
            lastSwipe = now;
            android.util.DisplayMetrics metrics = new android.util.DisplayMetrics();
            getSystemService(WindowManager.class).getDefaultDisplay().getRealMetrics(metrics);
            float x = metrics.widthPixels * 0.5f;
            float bottom = metrics.heightPixels * 0.76f, top = metrics.heightPixels * 0.28f;
            Path path = new Path(); path.moveTo(x, up ? bottom : top); path.lineTo(x, up ? top : bottom);
            GestureDescription gesture = new GestureDescription.Builder().addStroke(new GestureDescription.StrokeDescription(path, 0, 260)).build();
            try {
                boolean accepted = dispatchGesture(gesture, new GestureResultCallback() {
                    @Override public void onCompleted(GestureDescription g) { result[0] = "OK"; done.countDown(); }
                    @Override public void onCancelled(GestureDescription g) { result[0] = "ERR CANCELLED"; done.countDown(); }
                }, null);
                if (!accepted) { result[0] = "ERR GESTURE"; done.countDown(); }
            } catch (RuntimeException e) { result[0] = "ERR GESTURE"; done.countDown(); }
        });
        try { done.await(2, TimeUnit.SECONDS); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
        return result[0];
    }
}
