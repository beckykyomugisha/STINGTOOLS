import Navbar from '@/components/Navbar';
import HeroSection from '@/components/HeroSection';
import MetricsStrip from '@/components/MetricsStrip';
import FeaturesGrid from '@/components/FeaturesGrid';
import PlatformPreview from '@/components/PlatformPreview';
import StandardsBadges from '@/components/StandardsBadges';
import AfricaSection from '@/components/AfricaSection';
import PricingCards from '@/components/PricingCards';
import CtaBanner from '@/components/CtaBanner';
import Footer from '@/components/Footer';

export default function Page() {
  return (
    <main>
      <Navbar />
      <HeroSection />
      <MetricsStrip />
      <FeaturesGrid />
      <PlatformPreview />
      <StandardsBadges />
      <AfricaSection />
      <PricingCards />
      <CtaBanner />
      <Footer />
    </main>
  );
}
