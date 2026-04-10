# Auto Trade - Intraday Trading Signal System

A React + DaisyUI web dashboard for intraday trading signals in the Indian stock market (NSE). This application aggregates news/sentiment data and generates buy/sell signals for manual execution via Zerodha.

## Features

- **Trading Signals Panel**: Displays active BUY/SELL signals with entry price, target, stop-loss, and signal strength
- **Watchlist**: Track multiple stocks with live prices, changes, and sentiment indicators
- **Market News Feed**: Real-time news headlines with sentiment analysis and related stock tickers
- **Intraday Charts**: Basic candlestick charts for selected stocks using Recharts
- **Market Status**: Live Nifty 50 index value and market open/closed status
- **Responsive Design**: Mobile-friendly layout using DaisyUI components

## Tech Stack

- **Frontend**: React 18 + TypeScript + Vite
- **UI Framework**: DaisyUI + Tailwind CSS
- **Charts**: Recharts
- **Data Fetching**: React Query (ready for integration)

## Project Structure

```
auto-trade/
├── frontend/
│   ├── src/
│   │   ├── components/
│   │   │   ├── Dashboard.tsx      # Main dashboard layout
│   │   │   ├── Header.tsx        # Market status header
│   │   │   ├── SignalsPanel.tsx # Trading signals table
│   │   │   ├── Watchlist.tsx     # Stock watchlist
│   │   │   ├── NewsFeed.tsx      # Market news feed
│   │   │   └── StockChart.tsx    # Intraday chart
│   │   ├── data/
│   │   │   └── mockData.ts       # Mock data for development
│   │   ├── App.tsx
│   │   └── main.tsx
│   └── package.json
└── README.md
```

## Getting Started

### Prerequisites

- Node.js 18+ and npm

### Installation

1. Navigate to the frontend directory:
```bash
cd frontend
```

2. Install dependencies:
```bash
npm install
```

3. Start the development server:
```bash
npm run dev
```

4. Open your browser and navigate to `http://localhost:5173`

## Current Status

This is Phase 1 (UI Prototype) with mock data. The application displays:

- 5 sample trading signals (BUY/SELL)
- 6 stocks in the watchlist
- 5 market news items
- Interactive intraday charts

## Next Steps (Future Phases)

- **Phase 2**: Indian news RSS feeds with full NSE stock matching and sentiment analysis
- **Phase 3**: Implement signal generation engine with technical indicators
- **Phase 4**: Connect to Zerodha Kite API for real-time data
- **Phase 5**: Add backtesting and performance tracking

## Notes

- Currently uses mock data for demonstration
- Manual execution only (no automated trading)
- No SEBI registration required for signal-only system
- Designed for Indian stock market (NSE)

## License

MIT
