package com.cottonbrowser.mobile;

import java.io.StringReader;
import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;

/** Regression cases for false positives, player resources, popup targets and EasyList exceptions. */
public final class RequestBlocklistChecks {
    public static void main(String[] args) throws Exception {
        RequestBlocklist sample = RequestBlocklist.parse(new StringReader("ads.example\n"),
            new StringReader("||ads.example/required.js\t\tnews.example\t\t\n"
                + "||ads.example/health^\t\t\t\t\n"));
        require(blocked(sample, "https://ads.example/banner.js", "news.example"), "Ad request must block");
        require(!blocked(sample, "https://img.ads.example/required.js", "news.example"), "Origin exception missing");
        require(blocked(sample, "https://ads.example/required.js", "other.example"), "Origin exception too broad");
        require(!blocked(sample, "https://ads.example/required.js", null), "Unknown origin must preserve exception");
        require(!blocked(sample, "https://ads.example/health?status=1", "other.example"), "Separator filter failed");
        require(blocked(sample, "https://ads.example/healthcare", "other.example"), "Separator must not match substring");
        require(!blocked(sample, "https://notads.example/banner.js", "news.example"), "Similar host blocked");
        require(!blocked(sample, "https://ads.example.evil.test/banner.js", "news.example"), "Suffix host blocked");
        require(!RequestBlocklist.compileUrlFilter("||ads.example/required.js")
            .matcher("https://content.test/?url=https://ads.example/required.js").find(), "Anchor matched query");
        require(RequestBlocklist.compileUrlFilter("|https://ads.example/banner*^")
            .matcher("https://ads.example/banner123?x=1").find(), "Wildcard URL filter failed");

        Path assets = Path.of(args[0]);
        RequestBlocklist bundled = RequestBlocklist.parse(new StringReader(
            Files.readString(assets.resolve("blocklist.txt")) + "\n" +
            Files.readString(assets.resolve("easylist_domains.txt"))),
            Files.newBufferedReader(assets.resolve("easylist_allow.tsv")));
        require(bundled.size() >= 46000, "Expanded protection asset missing");
        for (String url : new String[]{"https://ads.doubleclick.net/banner.js", "https://popads.net/pop.js",
                "https://000491b06a.com/popup", "https://adnxs.com/ad.js"})
            require(blocked(bundled, url, "example.org"), "Ad/popup not blocked: " + url);
        for (String url : new String[]{"https://www.youtube.com/youtubei/v1/player",
                "https://rr1.googlevideo.com/videoplayback?id=123", "https://i.ytimg.com/vi/123/default.jpg",
                "https://accounts.google.com/login", "https://example.org/assets/video.mp4"})
            require(!blocked(bundled, url, "m.youtube.com"), "Legitimate media/login blocked: " + url);
        require(!blocked(bundled, "https://securepubads.g.doubleclick.net/tag/js/gpt.js", "bloomberg.com"),
            "Bundled compatibility exception missing");
        require(blocked(bundled, "https://securepubads.g.doubleclick.net/tag/js/gpt.js", "example.org"),
            "Compatibility exception leaked to unrelated origin");
        System.out.println("Android request-filter checks passed (" + bundled.size() + " domains).");
    }

    private static boolean blocked(RequestBlocklist rules, String url, String origin) {
        return rules.matches(url, URI.create(url).getHost(), origin);
    }

    private static void require(boolean value, String message) {
        if (!value) throw new AssertionError(message);
    }
}
