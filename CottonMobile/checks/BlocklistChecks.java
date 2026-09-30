package com.cottonbrowser.mobile;

import java.io.IOException;
import java.io.StringReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/** Standalone checks for the rules shipped in the Android APK. */
public final class BlocklistChecks {
    private BlocklistChecks() { }

    public static void main(String[] args) throws IOException {
        if (args.length != 2) {
            throw new IllegalArgumentException("Usage: BlocklistChecks <mobile-blocklist> <desktop-blocklist>");
        }

        DomainBlocklist sample = DomainBlocklist.parse(new StringReader(
            "# Comment\n"
                + "\n"
                + "ads.example.test\n"
                + "0.0.0.0 tracker.example.test\n"
                + "127.0.0.1 other.example.test\n"));

        require(sample.matchesHost("ads.example.test"), "Exact domain must match");
        require(sample.matchesHost("img.ads.example.test"), "Subdomain must match");
        require(sample.matchesHost("ADS.EXAMPLE.TEST."), "Case and trailing dot must normalize");
        require(sample.matchesHost("tracker.example.test"), "Hosts-file entry must match");
        require(sample.matchesHost("other.example.test"), "127.0.0.1 entry must match");
        require(!sample.matchesHost("notads.example.test"), "Similar suffix must not match");
        require(!sample.matchesHost("ads.example.test.evil.test"), "Parent suffix must not match");
        require(!sample.matchesHost("unlisted.example.test"), "Unlisted domain must not match");

        Path asset = Path.of(args[0]);
        try (var reader = Files.newBufferedReader(asset, StandardCharsets.UTF_8)) {
            DomainBlocklist bundled = DomainBlocklist.parse(reader);
            require(bundled.size() >= 50, "Bundled list is unexpectedly small");
            require(bundled.matchesHost("doubleclick.net"), "Bundled ad domain is missing");
            require(bundled.matchesHost("ads.doubleclick.net"), "Bundled subdomain is not blocked");
            require(!bundled.matchesHost("example.org"), "Benign domain was blocked");
            require(!bundled.matchesHost("youtube.com"), "YouTube playback host must remain available");
            require(!bundled.matchesHost("googlevideo.com"), "Video streams must remain available");
            try (var desktopReader = Files.newBufferedReader(Path.of(args[1]), StandardCharsets.UTF_8)) {
                DomainBlocklist desktop = DomainBlocklist.parse(desktopReader);
                require(bundled.size() == desktop.size(), "Mobile and desktop rules diverged");
                for (String line : Files.readAllLines(Path.of(args[1]), StandardCharsets.UTF_8)) {
                    String domain = line.split("#", 2)[0].trim();
                    if (!domain.isEmpty()) {
                        require(bundled.matchesHost(domain), "Desktop domain missing on mobile: " + domain);
                    }
                }
            }
            System.out.println("Android ad-block checks passed (" + bundled.size() + " domains).");
        }
    }

    private static void require(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError(message);
        }
    }
}
